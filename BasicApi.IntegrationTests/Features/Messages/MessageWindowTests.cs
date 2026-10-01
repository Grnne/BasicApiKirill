using System.Net;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;

namespace BasicApi.IntegrationTests.Features.Messages;

/// <summary>History opened around a message (search, reply) and paged forward from it.</summary>
public class MessageWindowTests(PostgresFixture db) : DbTest(db)
{
    private async Task<(ApiFactory Factory, HttpClient Alice, HttpClient Bob, Guid Chat, List<JsonElement> Sent)> ArrangeAsync(int count)
    {
        var factory = new ApiFactory(Db.ConnectionString,
            new Dictionary<string, string?> { ["RateLimiting:CommandsPer10Seconds"] = "1000" });
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        var aliceApi = factory.CreateClient(alice.Token);
        var sent = new List<JsonElement>();
        for (var i = 1; i <= count; i++)
            sent.Add(await aliceApi.PostJsonAsync($"/api/chats/{chat}/messages", new { text = $"m{i}" }));
        return (factory, aliceApi, factory.CreateClient(bob.Token), chat, sent);
    }

    private static List<string> Texts(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(m => m.GetProperty("text").GetString()!)];

    [Fact]
    public async Task Context_IsAWindowAroundTheMessage_WithWaysBothSides()
    {
        var (factory, alice, bob, chat, sent) = await ArrangeAsync(20);
        await using var _ = factory;
        using var __ = alice;
        using var ___ = bob;

        var page = await bob.GetJsonAsync($"/api/chats/{chat}/messages/{sent[9].Id()}/context?limit=6");

        Assert.Equal(["m7", "m8", "m9", "m10", "m11", "m12"], Texts(page));
        Assert.True(page.GetProperty("hasMore").GetBoolean());
        Assert.True(page.GetProperty("hasNewer").GetBoolean());

        // Older from the cursor, newer from the last seq: no gaps, no repeats.
        var older = await bob.GetJsonAsync($"/api/chats/{chat}/messages/cursor?limit=6&cursor={page.GetProperty("nextCursor").GetString()}");
        Assert.Equal(["m1", "m2", "m3", "m4", "m5", "m6"], Texts(older));
        var lastSeq = page.GetProperty("items").EnumerateArray().Last().GetProperty("seq").GetInt64();
        var newer = await bob.GetJsonAsync($"/api/chats/{chat}/messages/after?seq={lastSeq}&limit=6");
        Assert.Equal(["m13", "m14", "m15", "m16", "m17", "m18"], Texts(newer));
        Assert.True(newer.GetProperty("hasNewer").GetBoolean());

        var single = await bob.GetJsonAsync($"/api/chats/{chat}/messages/{sent[9].Id()}/context?limit=1");
        Assert.Equal(["m10"], Texts(single));
        Assert.True(single.GetProperty("hasMore").GetBoolean());
        Assert.True(single.GetProperty("hasNewer").GetBoolean());
    }

    [Fact]
    public async Task After_TheNewestPage_HasNoNewer_AndSkipsDeletedAndHidden()
    {
        var (factory, alice, bob, chat, sent) = await ArrangeAsync(5);
        await using var _ = factory;
        using var __ = alice;
        using var ___ = bob;
        (await alice.DeleteAsync($"/api/chats/{chat}/messages/{sent[2].Id()}?forEveryone=true")).EnsureSuccessStatusCode();
        (await bob.DeleteAsync($"/api/chats/{chat}/messages/{sent[3].Id()}")).EnsureSuccessStatusCode();

        var page = await bob.GetJsonAsync($"/api/chats/{chat}/messages/after?seq={sent[0].GetProperty("seq").GetInt64()}&limit=10");

        Assert.Equal(["m2", "m5"], Texts(page));
        Assert.False(page.GetProperty("hasNewer").GetBoolean());
    }

    [Fact]
    public async Task Context_OfAMessageTheViewerCannotSee_Is404()
    {
        var (factory, alice, bob, chat, sent) = await ArrangeAsync(3);
        await using var _ = factory;
        using var __ = alice;
        using var ___ = bob;
        (await bob.DeleteAsync($"/api/chats/{chat}/messages/{sent[1].Id()}")).EnsureSuccessStatusCode();

        var hidden = await bob.GetAsync($"/api/chats/{chat}/messages/{sent[1].Id()}/context");
        var unknown = await bob.GetAsync($"/api/chats/{chat}/messages/{Guid.NewGuid()}/context");

        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal("MESSAGE_NOT_FOUND", await hidden.ErrorCodeAsync());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task NotAMember_Is403()
    {
        var (factory, alice, bob, chat, sent) = await ArrangeAsync(1);
        await using var _ = factory;
        using var __ = alice;
        using var ___ = bob;
        var carol = await factory.RegisterAsync("carol");
        using var carolApi = factory.CreateClient(carol.Token);

        var context = await carolApi.GetAsync($"/api/chats/{chat}/messages/{sent[0].Id()}/context");
        var after = await carolApi.GetAsync($"/api/chats/{chat}/messages/after?seq=0");

        Assert.Equal(HttpStatusCode.Forbidden, context.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
    }
}
