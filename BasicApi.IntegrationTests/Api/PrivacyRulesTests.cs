using System.Net;
using System.Net.Http.Json;
using BasicApi.IntegrationTests.Infrastructure;

namespace BasicApi.IntegrationTests.Api;

/// <summary>Who may start a private chat and who may add to groups (plan 2, F5.2, D11).</summary>
public class PrivacyRulesTests(PostgresFixture db) : DbTest(db)
{
    private static Dictionary<string, string?> Settings => new() { ["RateLimiting:CommandsPer10Seconds"] = "1000" };

    private static Task<HttpResponseMessage> PrivacyAsync(HttpClient api, object body) =>
        api.PutAsJsonAsync("/api/users/me/privacy", body);

    [Fact]
    public async Task PrivateChat_FollowsTheMessagesSetting_ButAnExistingOneKeepsWorking()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carl = await factory.RegisterAsync("carl");
        var dave = await factory.RegisterAsync("dave");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        using var carlApi = factory.CreateClient(carl.Token);
        using var daveApi = factory.CreateClient(dave.Token);
        var existing = await Data.PrivateChatAsync(alice.UserId, dave.UserId);
        await Data.GroupChatAsync("club", [alice.UserId, bob.UserId]);

        await PrivacyAsync(aliceApi, new { messages = "contacts" });
        // Bob shares a group with Alice; Carl does not.
        Assert.Equal(HttpStatusCode.Created, (await bobApi.PostAsync($"/api/chats/private/{alice.UserId}", null)).StatusCode);
        var carlTry = await carlApi.PostAsync($"/api/chats/private/{alice.UserId}", null);
        Assert.Equal(HttpStatusCode.Forbidden, carlTry.StatusCode);
        Assert.Equal("PRIVACY_RESTRICTED", await carlTry.ErrorCodeAsync());

        await PrivacyAsync(aliceApi, new { messages = "nobody" });
        // The chat with Dave was there before: it opens, and messages go through.
        Assert.Equal(HttpStatusCode.OK, (await daveApi.PostAsync($"/api/chats/private/{alice.UserId}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await daveApi.PostAsJsonAsync($"/api/chats/{existing}/messages", new { text = "still here" })).StatusCode);
        // Alice herself may still start chats: the setting is about who writes to her.
        Assert.Equal(HttpStatusCode.Created, (await aliceApi.PostAsync($"/api/chats/private/{carl.UserId}", null)).StatusCode);
    }

    [Fact]
    public async Task GroupAdd_FollowsTheSetting_AndNamesWhoRefused()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carl = await factory.RegisterAsync("carl");
        var dave = await factory.RegisterAsync("dave");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        using var carlApi = factory.CreateClient(carl.Token);
        await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        await PrivacyAsync(bobApi, new { groupAdd = "contacts" });
        await PrivacyAsync(carlApi, new { groupAdd = "nobody" });

        // Carl allows nobody: the whole group is refused, and the answer says whom.
        var refused = await aliceApi.PostAsJsonAsync("/api/chats/groups",
            new { title = "team", memberIds = new[] { bob.UserId, carl.UserId, dave.UserId } });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        var problem = await refused.ReadJsonAsync();
        Assert.Equal("PRIVACY_RESTRICTED", problem.GetProperty("errorCode").GetString());
        Assert.Equal([carl.UserId], problem.GetProperty("userIds").EnumerateArray().Select(u => u.GetGuid()));

        // Bob allows contacts: Alice shares a chat with him, Dave does not.
        var group = (await aliceApi.PostJsonAsync("/api/chats/groups",
            new { title = "team", memberIds = new[] { bob.UserId, dave.UserId } })).Id("chatId");
        using var daveApi = factory.CreateClient(dave.Token);
        await PrivacyAsync(aliceApi, new { groupAdd = "contacts" });
        var davesGroup = (await daveApi.PostJsonAsync("/api/chats/groups", new { title = "d", memberIds = Array.Empty<Guid>() })).Id("chatId");
        // Dave now shares the group "team" with Alice — so he may add her.
        Assert.Equal(HttpStatusCode.OK,
            (await daveApi.PostAsJsonAsync($"/api/chats/{davesGroup}/members", new { userIds = new[] { alice.UserId } })).StatusCode);
        var carlRefused = await aliceApi.PostAsJsonAsync($"/api/chats/{group}/members", new { userIds = new[] { carl.UserId } });
        Assert.Equal("PRIVACY_RESTRICTED", await carlRefused.ErrorCodeAsync());
        Assert.DoesNotContain(carl.UserId,
            (await aliceApi.GetJsonAsync($"/api/chats/{group}/members")).EnumerateArray().Select(m => m.Id("userId")));
    }
}
