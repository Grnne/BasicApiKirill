using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;

namespace BasicApi.IntegrationTests.Features.Chats;

/// <summary>Pinned, archived and muted chats.</summary>
public class ChatOrganizationTests(PostgresFixture db) : DbTest(db)
{
    private static Dictionary<string, string?> Settings => new() { ["RateLimiting:CommandsPer10Seconds"] = "1000" };

    private static async Task<List<Guid>> PageAsync(HttpClient api, string query = "") =>
        [.. (await api.GetJsonAsync($"/api/chats/page?limit=50{query}")).GetProperty("items").EnumerateArray().Select(c => c.Id("chatId"))];

    private static Task<HttpResponseMessage> PinAsync(HttpClient api, Guid chatId, bool pinned = true) =>
        api.PutAsJsonAsync($"/api/chats/{chatId}/pinned", new { pinned });

    private async Task<(ApiFactory Factory, AuthResult Alice, HttpClient Api, Guid[] Chats)> SetUpAsync(int chats)
    {
        var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        var ids = new Guid[chats];
        for (var i = 0; i < chats; i++)
        {
            var other = await Data.UserAsync($"user{i}");
            // Older chats first: chat 0 is the oldest, the last one is on top of the list.
            ids[i] = await Data.PrivateChatAsync(alice.UserId, other, TestData.T0.AddMinutes(i));
        }
        return (factory, alice, factory.CreateClient(alice.Token), ids);
    }

    [Fact]
    public async Task Pinned_GoOnTop_InTheirOrder_AndThePagesGoOnWithoutThem()
    {
        var (factory, _, api, chats) = await SetUpAsync(5);
        await using var _f = factory;
        using var _a = api;

        await PinAsync(api, chats[1]);
        var pinned = await (await PinAsync(api, chats[0])).ReadJsonAsync();
        Assert.Equal([chats[0], chats[1]], pinned.GetProperty("chatIds").EnumerateArray().Select(c => c.GetGuid()));

        Assert.Equal([chats[0], chats[1], chats[4], chats[3], chats[2]], await PageAsync(api));
        Assert.Equal([chats[0], chats[1], chats[4], chats[3], chats[2]],
            (await api.GetJsonAsync("/api/chats")).EnumerateArray().Select(c => c.Id("chatId")));
        var item = await api.ChatItemAsync(chats[0]);
        Assert.Equal(1, item.GetProperty("pinnedPosition").GetInt32());

        // Pinned chats do not count toward the limit and do not come back on later pages.
        var first = await api.GetJsonAsync("/api/chats/page?limit=2");
        Assert.Equal([chats[0], chats[1], chats[4], chats[3]], first.GetProperty("items").EnumerateArray().Select(c => c.Id("chatId")));
        var second = await api.GetJsonAsync($"/api/chats/page?limit=2&cursor={first.GetProperty("nextCursor").GetString()}");
        Assert.Equal([chats[2]], second.GetProperty("items").EnumerateArray().Select(c => c.Id("chatId")));

        Assert.Equal("INVALID_REQUEST", await (await api.PutAsJsonAsync("/api/chats/pinned", new { chatIds = new[] { chats[1] } })).ErrorCodeAsync());
        var reordered = await api.PutAsJsonAsync("/api/chats/pinned", new { chatIds = new[] { chats[1], chats[0] } });
        Assert.Equal(HttpStatusCode.OK, reordered.StatusCode);
        Assert.Equal([chats[1], chats[0], chats[4], chats[3], chats[2]], await PageAsync(api));

        await PinAsync(api, chats[1], pinned: false);
        Assert.Equal([chats[0], chats[4], chats[3], chats[2], chats[1]], await PageAsync(api));
        var changes = await api.JournalAsync("PinnedChatsChanged");
        Assert.Equal(4, changes.Count);
        Assert.Equal([chats[0]], changes[^1].GetProperty("chatIds").EnumerateArray().Select(c => c.GetGuid()));
    }

    [Fact]
    public async Task Pins_HaveALimit()
    {
        var (factory, _, api, chats) = await SetUpAsync(11);
        await using var _f = factory;
        using var _a = api;

        foreach (var chat in chats[..10])
            Assert.Equal(HttpStatusCode.OK, (await PinAsync(api, chat)).StatusCode);
        Assert.Equal("TOO_MANY_PINNED", await (await PinAsync(api, chats[10])).ErrorCodeAsync());
        Assert.Equal(HttpStatusCode.OK, (await PinAsync(api, chats[0])).StatusCode); // already pinned
    }

    [Fact]
    public async Task Archive_HidesAChat_UntilAMessage_UnlessMuted()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carl = await factory.RegisterAsync("carl");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        using var carlApi = factory.CreateClient(carl.Token);
        var withBob = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        var withCarl = await Data.PrivateChatAsync(alice.UserId, carl.UserId);

        await PinAsync(aliceApi, withBob);
        var archived = await (await aliceApi.PutAsJsonAsync($"/api/chats/{withBob}/archived", new { archived = true })).ReadJsonAsync();
        Assert.True(archived.GetProperty("archived").GetBoolean());
        Assert.Equal([withCarl], await PageAsync(aliceApi));
        Assert.Equal([withBob], await PageAsync(aliceApi, "&archived=true"));
        Assert.True((await aliceApi.ChatItemAsync(withBob)).GetProperty("archived").GetBoolean());
        Assert.Equal(JsonValueKind.Null, (await aliceApi.ChatItemAsync(withBob)).GetProperty("pinnedPosition").ValueKind);

        // Bob writes: the chat is back, and Alice's devices learn it.
        await bobApi.PostJsonAsync($"/api/chats/{withBob}/messages", new { text = "hi" });
        Assert.Contains(withBob, await PageAsync(aliceApi));
        var back = (await aliceApi.JournalAsync("ChatStateChanged"))[^1];
        Assert.Equal(withBob, back.Id("chatId"));
        Assert.False(back.GetProperty("archived").GetBoolean());

        // Muted: it stays in the archive, and the unread counter still counts.
        await aliceApi.PutAsJsonAsync($"/api/chats/{withCarl}/muted", new { muted = true });
        await aliceApi.PutAsJsonAsync($"/api/chats/{withCarl}/archived", new { archived = true });
        await carlApi.PostJsonAsync($"/api/chats/{withCarl}/messages", new { text = "psst" });
        Assert.Equal([withCarl], await PageAsync(aliceApi, "&archived=true"));
        Assert.Equal(1, (await aliceApi.ChatItemAsync(withCarl)).GetProperty("unreadCount").GetInt32());
    }

    [Fact]
    public async Task Mute_UntilAMoment_OrForGood()
    {
        var (factory, _, api, chats) = await SetUpAsync(1);
        await using var _f = factory;
        using var _a = api;
        var until = DateTime.UtcNow.AddHours(8);

        var timed = await (await api.PutAsJsonAsync($"/api/chats/{chats[0]}/muted", new { muted = true, until })).ReadJsonAsync();
        Assert.True(timed.GetProperty("isMuted").GetBoolean());
        Assert.InRange(timed.GetProperty("mutedUntil").GetDateTime().ToUniversalTime(), until.AddSeconds(-1), until.AddSeconds(1));

        var forever = await (await api.PutAsJsonAsync($"/api/chats/{chats[0]}/muted", new { muted = true })).ReadJsonAsync();
        Assert.True(forever.GetProperty("isMuted").GetBoolean());
        Assert.Equal(JsonValueKind.Null, forever.GetProperty("mutedUntil").ValueKind);
        Assert.True((await api.ChatItemAsync(chats[0])).GetProperty("isMuted").GetBoolean());

        Assert.Equal("INVALID_REQUEST", await (await api.PutAsJsonAsync($"/api/chats/{chats[0]}/muted",
            new { muted = true, until = DateTime.UtcNow.AddMinutes(-1) })).ErrorCodeAsync());
        var unmuted = await (await api.PutAsJsonAsync($"/api/chats/{chats[0]}/muted", new { muted = false })).ReadJsonAsync();
        Assert.False(unmuted.GetProperty("isMuted").GetBoolean());
        Assert.Equal(3, (await api.JournalAsync("ChatStateChanged")).Count);
        Assert.Equal("NOT_A_MEMBER", await (await api.PutAsJsonAsync($"/api/chats/{Guid.NewGuid()}/muted", new { muted = true })).ErrorCodeAsync());
    }
}
