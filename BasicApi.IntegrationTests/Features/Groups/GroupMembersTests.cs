using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;
using Dapper;
using Microsoft.AspNetCore.SignalR.Client;
using Npgsql;

namespace BasicApi.IntegrationTests.Features.Groups;

/// <summary>Adding, removing and leaving.</summary>
public class GroupMembersTests(PostgresFixture db) : DbTest(db)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static Dictionary<string, string?> Settings(int maxMembers = 500) => new()
    {
        ["RateLimiting:CommandsPer10Seconds"] = "1000",
        ["Groups:MaxMembers"] = maxMembers.ToString()
    };

    private static async Task<Guid> CreateGroupAsync(HttpClient api, params Guid[] members) =>
        (await api.PostJsonAsync("/api/chats/groups", new { title = "team", memberIds = members })).Id("chatId");

    private static Task<HttpResponseMessage> AddAsync(HttpClient api, Guid chatId, params Guid[] userIds) =>
        api.PostAsJsonAsync($"/api/chats/{chatId}/members", new { userIds });

    private static async Task<string?> CodeAsync(Task<HttpResponseMessage> call) => await (await call).ErrorCodeAsync();

    [Fact]
    public async Task AddedMember_GetsTheChat_SeesTheHistory_AndOnlyTheNewIsUnread()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings());
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var dave = await factory.RegisterAsync("dave");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        using var daveApi = factory.CreateClient(dave.Token);
        var chatId = await CreateGroupAsync(aliceApi, bob.UserId);
        (await aliceApi.PostJsonAsync($"/api/chats/{chatId}/messages", new { text = "before dave" })).GetProperty("id");

        var daveGotChat = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var daveHub = factory.CreateHubConnection(dave.Token);
        daveHub.On<JsonElement>("ChatCreated", c => daveGotChat.TrySetResult(c));
        await daveHub.StartAsync();
        var bobHeard = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var bobHub = factory.CreateHubConnection(bob.Token);
        bobHub.On<JsonElement>("MemberAdded", a => bobHeard.TrySetResult(a));
        await bobHub.StartAsync();

        var response = await AddAsync(bobApi, chatId, dave.UserId, bob.UserId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var added = Assert.Single((await response.ReadJsonAsync()).EnumerateArray());
        Assert.Equal(dave.UserId, added.Id("userId"));
        Assert.Equal("member", added.GetProperty("role").GetString());

        var card = await daveGotChat.Task.WaitAsync(Wait);
        Assert.Equal(chatId, card.Id("chatId"));
        Assert.Equal(1, card.GetProperty("unreadCount").GetInt32());
        Assert.Equal("Добавлен участник: dave", card.GetProperty("lastMessage").GetProperty("text").GetString());

        var announced = await bobHeard.Task.WaitAsync(Wait);
        Assert.Equal(bob.UserId, announced.Id("addedBy"));
        Assert.Equal([dave.UserId], announced.GetProperty("members").EnumerateArray().Select(m => m.Id("userId")));

        var history = await daveApi.HistoryAsync(chatId);
        Assert.Contains(history, m => m.GetProperty("text").GetString() == "before dave");
        var system = history[^1];
        Assert.Equal("members_added", system.GetProperty("action").GetProperty("type").GetString());
        Assert.Equal([dave.UserId], system.GetProperty("action").GetProperty("userIds").EnumerateArray().Select(u => u.GetGuid()));

        // Adding again: nobody new, nothing sent.
        Assert.Empty((await (await AddAsync(aliceApi, chatId, dave.UserId)).ReadJsonAsync()).EnumerateArray());
        Assert.Single(await bobApi.JournalAsync("MemberAdded"));
        Assert.Empty(await daveApi.JournalAsync("MemberAdded"));
    }

    [Fact]
    public async Task Adding_IsChecked()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings(maxMembers: 3));
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carol = await Data.UserAsync("carol");
        var dave = await Data.UserAsync("dave");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        var chatId = await CreateGroupAsync(aliceApi, bob.UserId);

        Assert.Equal("INVALID_REQUEST", await CodeAsync(AddAsync(aliceApi, chatId, alice.UserId)));
        Assert.Equal("USER_NOT_FOUND", await CodeAsync(AddAsync(aliceApi, chatId, Guid.NewGuid())));
        Assert.Equal("TOO_MANY_MEMBERS", await CodeAsync(AddAsync(aliceApi, chatId, carol, dave)));
        (await aliceApi.PutAsJsonAsync($"/api/chats/{chatId}/members/{bob.UserId}/permissions", new { addMembers = false }))
            .EnsureSuccessStatusCode();
        Assert.Equal("PERMISSION_DENIED", await CodeAsync(AddAsync(bobApi, chatId, carol)));
        var privateChat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        Assert.Equal("NOT_A_GROUP", await CodeAsync(AddAsync(aliceApi, privateChat, carol)));
    }

    [Fact]
    public async Task ConcurrentAdds_KeepTheLimit()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings(maxMembers: 4));
        var alice = await factory.RegisterAsync("alice");
        using var api = factory.CreateClient(alice.Token);
        var others = new List<Guid>();
        for (var i = 0; i < 5; i++)
            others.Add(await Data.UserAsync($"u{i}"));
        var chatId = await CreateGroupAsync(api, others[0]);

        var results = await Task.WhenAll(
            AddAsync(api, chatId, others[1], others[2]),
            AddAsync(api, chatId, others[3], others[4]));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.BadRequest);
        Assert.Equal(4, (await api.GetJsonAsync($"/api/chats/{chatId}/members")).GetArrayLength());
    }

    [Fact]
    public async Task RemovedMember_LosesTheChat_AndItsLiveEvents()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings());
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carol = await factory.RegisterAsync("carol");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        using var carolApi = factory.CreateClient(carol.Token);
        var chatId = await CreateGroupAsync(aliceApi, bob.UserId, carol.UserId);
        var privateChat = (await aliceApi.PostJsonAsync($"/api/chats/private/{bob.UserId}")).Id("chatId");

        // Bob has the group open: his connection is in its hub group.
        var seen = new List<string>();
        var removed = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var marker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var bobHub = factory.CreateHubConnection(bob.Token);
        bobHub.On<JsonElement>("MessageCreated", m =>
        {
            lock (seen) seen.Add(m.GetProperty("text").GetString()!);
            if (m.GetProperty("text").GetString() == "marker") marker.TrySetResult();
        });
        bobHub.On<JsonElement>("MemberRemoved", r => removed.TrySetResult(r));
        await bobHub.StartAsync();
        await bobHub.InvokeAsync("JoinChat", chatId);
        await bobHub.InvokeAsync("JoinChat", privateChat);

        // A member may not remove another member; the owner may.
        Assert.Equal("PERMISSION_DENIED", await CodeAsync(carolApi.DeleteAsync($"/api/chats/{chatId}/members/{bob.UserId}")));
        Assert.Equal(HttpStatusCode.NoContent, (await aliceApi.DeleteAsync($"/api/chats/{chatId}/members/{bob.UserId}")).StatusCode);

        var notice = await removed.Task.WaitAsync(Wait);
        Assert.Equal(alice.UserId, notice.Id("removedBy"));

        // The group goes on without him; events of the private chat still reach him — and come after.
        (await aliceApi.PostJsonAsync($"/api/chats/{chatId}/messages", new { text = "secret plans" })).GetProperty("id");
        (await aliceApi.PostJsonAsync($"/api/chats/{privateChat}/messages", new { text = "marker" })).GetProperty("id");
        await marker.Task.WaitAsync(Wait);
        lock (seen)
            Assert.DoesNotContain("secret plans", seen);

        Assert.Equal(HttpStatusCode.Forbidden, (await bobApi.GetAsync($"/api/chats/{chatId}/messages/cursor")).StatusCode);
        Assert.DoesNotContain((await bobApi.GetJsonAsync("/api/chats")).EnumerateArray(), c => c.Id("chatId") == chatId);
        await Assert.ThrowsAsync<Microsoft.AspNetCore.SignalR.HubException>(() => bobHub.InvokeAsync("JoinChat", chatId));

        var history = await carolApi.HistoryAsync(chatId);
        Assert.Contains(history, m => m.GetProperty("action").ValueKind == JsonValueKind.Object &&
                                      m.GetProperty("action").GetProperty("type").GetString() == "member_removed");
        Assert.Single(await carolApi.JournalAsync("MemberRemoved"));
    }

    [Fact]
    public async Task OwnerLeaving_HandsTheGroupToTheOldestAdmin_AndTheLastOneDeletesIt()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings());
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carol = await factory.RegisterAsync("carol");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        using var carolApi = factory.CreateClient(carol.Token);
        var chatId = await CreateGroupAsync(aliceApi, bob.UserId, carol.UserId);
        (await aliceApi.PutAsJsonAsync($"/api/chats/{chatId}/members/{carol.UserId}/role", new { role = "admin" }))
            .EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NoContent, (await aliceApi.DeleteAsync($"/api/chats/{chatId}/members/{alice.UserId}")).StatusCode);

        // Carol, the admin, is the owner now — though Bob joined as early.
        var members = (await bobApi.GetJsonAsync($"/api/chats/{chatId}/members")).EnumerateArray().ToList();
        Assert.Equal([(carol.UserId, "owner"), (bob.UserId, "member")], members.Select(m => (m.Id("userId"), m.GetProperty("role").GetString())));
        Assert.Contains(await bobApi.JournalAsync("MemberUpdated"), u => u.GetProperty("member").Id("userId") == carol.UserId);
        Assert.Equal("member_left", (await bobApi.HistoryAsync(chatId))[^1].GetProperty("action").GetProperty("type").GetString());
        Assert.Single(await aliceApi.JournalAsync("MemberRemoved"));

        (await carolApi.DeleteAsync($"/api/chats/{chatId}/members/{carol.UserId}")).EnsureSuccessStatusCode();
        Assert.Equal("owner", (await bobApi.GetJsonAsync($"/api/chats/{chatId}")).GetProperty("myRole").GetString());
        (await bobApi.DeleteAsync($"/api/chats/{chatId}/members/{bob.UserId}")).EnsureSuccessStatusCode();

        await using var connection = new NpgsqlConnection(Db.ConnectionString);
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM chats WHERE id = @chatId", new { chatId }));
    }
}
