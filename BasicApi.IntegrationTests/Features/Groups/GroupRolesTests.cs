using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;
using Dapper;
using Npgsql;

namespace BasicApi.IntegrationTests.Features.Groups;

/// <summary>Roles and permissions in a group.</summary>
public class GroupRolesTests(PostgresFixture db) : DbTest(db)
{
    private static Dictionary<string, string?> Settings => new() { ["RateLimiting:CommandsPer10Seconds"] = "1000" };

    private sealed record Group(ApiFactory Factory, Guid ChatId, AuthResult Owner, AuthResult Bob, AuthResult Carol, AuthResult Dave)
    {
        public HttpClient Api(AuthResult user) => Factory.CreateClient(user.Token);
    }

    private async Task<Group> ArrangeAsync()
    {
        var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carol = await factory.RegisterAsync("carol");
        var dave = await factory.RegisterAsync("dave");
        using var api = factory.CreateClient(alice.Token);
        var chatId = (await api.PostJsonAsync("/api/chats/groups",
            new { title = "team", memberIds = new[] { bob.UserId, carol.UserId, dave.UserId } })).Id("chatId");
        return new Group(factory, chatId, alice, bob, carol, dave);
    }

    private static Task<HttpResponseMessage> SetRoleAsync(HttpClient api, Guid chatId, Guid userId, string role) =>
        api.PutAsJsonAsync($"/api/chats/{chatId}/members/{userId}/role", new { role });

    private static Task<HttpResponseMessage> SetPermissionsAsync(HttpClient api, Guid chatId, Guid userId, object permissions) =>
        api.PutAsJsonAsync($"/api/chats/{chatId}/members/{userId}/permissions", permissions);

    private static async Task Expect(HttpStatusCode status, string code, Task<HttpResponseMessage> call)
    {
        var response = await call;
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, await response.ErrorCodeAsync());
    }

    private static async Task Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task RestrictedMember_CannotWrite_UntilTheRestrictionIsLifted()
    {
        var g = await ArrangeAsync();
        await using var _ = g.Factory;
        using var owner = g.Api(g.Owner);
        using var bob = g.Api(g.Bob);

        var muted = await (await SetPermissionsAsync(owner, g.ChatId, g.Bob.UserId, new { sendMessages = false })).ReadJsonAsync();
        Assert.False(muted.GetProperty("permissions").GetProperty("sendMessages").GetBoolean());

        await Expect(HttpStatusCode.Forbidden, "PERMISSION_DENIED",
            bob.PostAsJsonAsync($"/api/chats/{g.ChatId}/messages", new { text = "let me speak" }));
        await Expect(HttpStatusCode.Forbidden, "PERMISSION_DENIED",
            bob.PostAsJsonAsync($"/api/chats/{g.ChatId}/typing", new { isTyping = true }));
        // Reading and reacting stay.
        Assert.NotEmpty(await bob.HistoryAsync(g.ChatId));

        var details = await bob.GetJsonAsync($"/api/chats/{g.ChatId}");
        Assert.Equal("member", details.GetProperty("myRole").GetString());
        Assert.False(details.GetProperty("myPermissions").GetProperty("sendMessages").GetBoolean());
        Assert.True(details.GetProperty("memberPermissions").GetProperty("sendMessages").GetBoolean());

        await Ok(SetPermissionsAsync(owner, g.ChatId, g.Bob.UserId, new { }));
        await Ok(bob.PostAsJsonAsync($"/api/chats/{g.ChatId}/messages", new { text = "thanks" }));

        // Everyone heard of both changes; the same change again is quiet.
        await Ok(SetPermissionsAsync(owner, g.ChatId, g.Bob.UserId, new { }));
        using var carol = g.Api(g.Carol);
        Assert.Equal(2, (await carol.JournalAsync("MemberUpdated")).Count);
    }

    [Fact]
    public async Task Admins_AreMadeByThoseAllowed_AndMayOnlyRestrictMembers()
    {
        var g = await ArrangeAsync();
        await using var _ = g.Factory;
        using var owner = g.Api(g.Owner);
        using var bob = g.Api(g.Bob);
        using var carol = g.Api(g.Carol);

        await Expect(HttpStatusCode.Forbidden, "PERMISSION_DENIED", SetRoleAsync(carol, g.ChatId, g.Dave.UserId, "admin"));

        var admin = await (await SetRoleAsync(owner, g.ChatId, g.Bob.UserId, "admin")).ReadJsonAsync();
        Assert.Equal("admin", admin.GetProperty("role").GetString());
        Assert.False(admin.GetProperty("permissions").GetProperty("addAdmins").GetBoolean());

        // An admin makes admins only when the owner allows it.
        await Expect(HttpStatusCode.Forbidden, "PERMISSION_DENIED", SetRoleAsync(bob, g.ChatId, g.Carol.UserId, "admin"));
        await Ok(SetPermissionsAsync(owner, g.ChatId, g.Bob.UserId, new { addAdmins = true }));
        await Ok(SetRoleAsync(bob, g.ChatId, g.Carol.UserId, "admin"));

        // ...but only the owner unmakes them, or restricts them.
        await Expect(HttpStatusCode.Forbidden, "PERMISSION_DENIED", SetRoleAsync(bob, g.ChatId, g.Carol.UserId, "member"));
        await Expect(HttpStatusCode.Forbidden, "PERMISSION_DENIED",
            SetPermissionsAsync(bob, g.ChatId, g.Carol.UserId, new { deleteMessages = false }));

        // An admin restricts a member; a member may not get admin permissions.
        await Ok(SetPermissionsAsync(bob, g.ChatId, g.Dave.UserId, new { addMembers = false }));
        await Expect(HttpStatusCode.BadRequest, "INVALID_PERMISSIONS",
            SetPermissionsAsync(owner, g.ChatId, g.Dave.UserId, new { deleteMessages = true }));
        await Expect(HttpStatusCode.Forbidden, "PERMISSION_DENIED", SetRoleAsync(owner, g.ChatId, g.Owner.UserId, "member"));
        await Expect(HttpStatusCode.NotFound, "MEMBER_NOT_FOUND", SetRoleAsync(owner, g.ChatId, Guid.NewGuid(), "admin"));
        await Expect(HttpStatusCode.BadRequest, "INVALID_ROLE", SetRoleAsync(owner, g.ChatId, g.Dave.UserId, "king"));

        var members = await carol.GetJsonAsync($"/api/chats/{g.ChatId}/members");
        Assert.Equal(["owner", "admin", "admin", "member"], members.EnumerateArray().Select(m => m.GetProperty("role").GetString()));
        var dave = members.EnumerateArray().Single(m => m.Id("userId") == g.Dave.UserId);
        Assert.False(dave.GetProperty("permissions").GetProperty("addMembers").GetBoolean());

        // Demoted: the admin overrides do not follow them.
        var demoted = await (await SetRoleAsync(owner, g.ChatId, g.Bob.UserId, "member")).ReadJsonAsync();
        Assert.False(demoted.GetProperty("permissions").GetProperty("addAdmins").GetBoolean());
        Assert.False(demoted.GetProperty("permissions").GetProperty("removeMembers").GetBoolean());
    }

    [Fact]
    public async Task HandingOver_MakesTheOldOwnerAnAdmin_AndConcurrentHandoversLeaveOneOwner()
    {
        var g = await ArrangeAsync();
        await using var _ = g.Factory;
        using var owner = g.Api(g.Owner);
        using var bob = g.Api(g.Bob);

        var results = await Task.WhenAll(
            SetRoleAsync(owner, g.ChatId, g.Bob.UserId, "owner"),
            SetRoleAsync(owner, g.ChatId, g.Carol.UserId, "owner"));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Forbidden);
        await using var connection = new NpgsqlConnection(Db.ConnectionString);
        var roles = (await connection.QueryAsync<(Guid UserId, string Role)>(
            "SELECT user_id, role FROM chat_members WHERE chat_id = @chatId", new { chatId = g.ChatId })).ToDictionary();
        Assert.Single(roles, r => r.Value == "owner");
        Assert.Equal("admin", roles[g.Owner.UserId]);

        // The new owner may unmake the old one; the old one may not touch the new one.
        var newOwner = roles.Single(r => r.Value == "owner").Key;
        using var newOwnerApi = newOwner == g.Bob.UserId ? g.Api(g.Bob) : g.Api(g.Carol);
        await Expect(HttpStatusCode.Forbidden, "PERMISSION_DENIED", SetRoleAsync(owner, g.ChatId, newOwner, "member"));
        await Ok(SetRoleAsync(newOwnerApi, g.ChatId, g.Owner.UserId, "member"));
    }

    [Fact]
    public async Task Admin_DeletesOthersMessagesAtAnyTime_AndTheLogRemembers()
    {
        var g = await ArrangeAsync();
        await using var _ = g.Factory;
        using var owner = g.Api(g.Owner);
        using var bob = g.Api(g.Bob);
        using var carol = g.Api(g.Carol);
        var old = await Data.MessageAsync(g.ChatId, g.Dave.UserId, "from last month", DateTime.UtcNow.AddDays(-30));
        var system = (await owner.HistoryAsync(g.ChatId)).First(m => m.GetProperty("type").GetString() == "system").Id();

        await Expect(HttpStatusCode.Forbidden, "NOT_MESSAGE_AUTHOR",
            carol.DeleteAsync($"/api/chats/{g.ChatId}/messages/{old}?forEveryone=true"));
        await Ok(SetRoleAsync(owner, g.ChatId, g.Bob.UserId, "admin"));
        await Ok(bob.DeleteAsync($"/api/chats/{g.ChatId}/messages/{old}?forEveryone=true"));
        await Ok(bob.DeleteAsync($"/api/chats/{g.ChatId}/messages/{system}?forEveryone=true"));

        Assert.DoesNotContain(await carol.HistoryAsync(g.ChatId), m => m.Id() == old || m.Id() == system);
        await using var connection = new NpgsqlConnection(Db.ConnectionString);
        var log = (await connection.QueryAsync<(string Action, Guid ActorId, Guid TargetUserId)>(
            "SELECT action, actor_id, target_user_id FROM chat_audit_log WHERE action = 'message_deleted' ORDER BY id")).ToList();
        Assert.Equal([("message_deleted", g.Bob.UserId, g.Dave.UserId), ("message_deleted", g.Bob.UserId, g.Owner.UserId)], log);
    }

    [Fact]
    public async Task GroupActions_InAPrivateChat_AreRefused()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var api = factory.CreateClient(alice.Token);
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);

        await Expect(HttpStatusCode.BadRequest, "NOT_A_GROUP", api.GetAsync($"/api/chats/{chat}/members"));
        await Expect(HttpStatusCode.BadRequest, "NOT_A_GROUP", SetRoleAsync(api, chat, bob.UserId, "admin"));

        var details = await api.GetJsonAsync($"/api/chats/{chat}");
        Assert.Equal(JsonValueKind.Null, details.GetProperty("myPermissions").ValueKind);
        Assert.Equal("member", details.GetProperty("myRole").GetString());

        // A stranger learns nothing about the chat's type.
        var carol = await factory.RegisterAsync("carol");
        using var carolApi = factory.CreateClient(carol.Token);
        await Expect(HttpStatusCode.Forbidden, "NOT_A_MEMBER", carolApi.GetAsync($"/api/chats/{chat}/members"));
    }
}
