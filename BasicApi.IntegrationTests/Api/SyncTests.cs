using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;
using BasicApi.Services.Events;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BasicApi.IntegrationTests.Api;

/// <summary>Sync after a connection drop (plan 1, 2.7).</summary>
public class SyncTests(PostgresFixture db) : DbTest(db)
{
    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private async Task<(ApiFactory Factory, AuthResult Alice, AuthResult Bob)> ArrangeAsync()
    {
        var factory = new ApiFactory(Db.ConnectionString,
            new Dictionary<string, string?> { ["RateLimiting:CommandsPer10Seconds"] = "1000" });
        return (factory, await factory.RegisterAsync("alice"), await factory.RegisterAsync("bob"));
    }

    private async Task ExecuteAsync(string sql, object? param = null)
    {
        await using var connection = new NpgsqlConnection(Db.ConnectionString);
        await connection.ExecuteAsync(sql, param);
    }

    [Fact]
    public async Task ClientThatWasOffline_GetsEveryMissedUpdateInOrder_ThenNothing()
    {
        var (factory, alice, bob) = await ArrangeAsync();
        await using var _ = factory;
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);

        // Bob took a snapshot and went offline.
        var state = await GetJsonAsync(bobApi, "/api/sync/state");
        var pts = state.GetProperty("pts").GetInt64();
        Assert.Empty(state.GetProperty("chats").EnumerateArray());

        // While he is away: Alice creates a chat and writes five messages.
        var chat = (await (await aliceApi.PostAsync($"/api/chats/private/{bob.UserId}", null))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("chatId").GetGuid();
        for (var i = 0; i < 5; i++)
            (await aliceApi.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text = $"m{i}" })).EnsureSuccessStatusCode();

        // He is back — catching up.
        var diff = await GetJsonAsync(bobApi, $"/api/sync?since={pts}");

        Assert.False(diff.GetProperty("snapshotRequired").GetBoolean());
        Assert.False(diff.GetProperty("hasMore").GetBoolean());
        var updates = diff.GetProperty("updates").EnumerateArray().ToList();
        Assert.Equal(Enumerable.Range(1, 6).Select(i => pts + i), updates.Select(u => u.GetProperty("pts").GetInt64()));
        Assert.Equal("ChatCreated", updates[0].GetProperty("type").GetString());
        Assert.Equal(alice.UserId, updates[0].GetProperty("payload").GetProperty("companionId").GetGuid());
        Assert.Equal(["m0", "m1", "m2", "m3", "m4"],
            updates.Skip(1).Select(u => u.GetProperty("payload").GetProperty("text").GetString()));
        Assert.All(updates.Skip(1), u => Assert.Equal("MessageCreated", u.GetProperty("type").GetString()));
        Assert.Equal(pts + 6, diff.GetProperty("pts").GetInt64());

        // A repeat request with the new pts — empty.
        var again = await GetJsonAsync(bobApi, $"/api/sync?since={diff.GetProperty("pts").GetInt64()}");
        Assert.Empty(again.GetProperty("updates").EnumerateArray());
        Assert.False(again.GetProperty("snapshotRequired").GetBoolean());
        Assert.Equal(pts + 6, again.GetProperty("pts").GetInt64());
    }

    [Fact]
    public async Task Difference_IsPaged()
    {
        var (factory, alice, bob) = await ArrangeAsync();
        await using var _ = factory;
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        for (var i = 0; i < 5; i++)
            (await aliceApi.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text = $"m{i}" })).EnsureSuccessStatusCode();

        var texts = new List<string>();
        long since = 0;
        bool hasMore;
        do
        {
            var page = await GetJsonAsync(bobApi, $"/api/sync?since={since}&limit=2");
            texts.AddRange(page.GetProperty("updates").EnumerateArray()
                .Select(u => u.GetProperty("payload").GetProperty("text").GetString()!));
            since = page.GetProperty("pts").GetInt64();
            hasMore = page.GetProperty("hasMore").GetBoolean();
        } while (hasMore);

        Assert.Equal(["m0", "m1", "m2", "m3", "m4"], texts);
    }

    [Fact]
    public async Task Snapshot_AndItsPts_AreConsistent()
    {
        var (factory, alice, bob) = await ArrangeAsync();
        await using var _ = factory;
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        for (var i = 0; i < 3; i++)
            (await aliceApi.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text = $"m{i}" })).EnsureSuccessStatusCode();

        var state = await GetJsonAsync(bobApi, "/api/sync/state");

        Assert.Equal(3, state.GetProperty("pts").GetInt64());
        var item = Assert.Single(state.GetProperty("chats").EnumerateArray());
        Assert.Equal(3, item.GetProperty("unreadCount").GetInt32());
        Assert.Equal("m2", item.GetProperty("lastMessage").GetProperty("text").GetString());
        Assert.Empty((await GetJsonAsync(bobApi, "/api/sync?since=3")).GetProperty("updates").EnumerateArray());
    }

    [Fact]
    public async Task TooOldOrForeignPts_AsksForASnapshot()
    {
        var (factory, alice, bob) = await ArrangeAsync();
        await using var _ = factory;
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        for (var i = 0; i < 3; i++)
            (await aliceApi.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text = $"m{i}" })).EnsureSuccessStatusCode();

        // The start of the journal was purged by retention.
        await ExecuteAsync("DELETE FROM user_updates WHERE user_id = @bob AND pts = 1", new { bob = bob.UserId });

        var purged = await GetJsonAsync(bobApi, "/api/sync?since=0");
        Assert.True(purged.GetProperty("snapshotRequired").GetBoolean());
        Assert.Empty(purged.GetProperty("updates").EnumerateArray());

        // Beyond the purged part — as usual.
        Assert.Equal(2, (await GetJsonAsync(bobApi, "/api/sync?since=1")).GetProperty("updates").GetArrayLength());

        // pts from the future (another database, someone else's journal) — also a snapshot.
        Assert.True((await GetJsonAsync(bobApi, "/api/sync?since=100")).GetProperty("snapshotRequired").GetBoolean());

        var negative = await bobApi.GetAsync("/api/sync?since=-1");
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
        Assert.Contains("INVALID_PTS", await negative.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ack_IsStoredPerDevice_AndOnlyMovesForward()
    {
        var (factory, alice, bob) = await ArrangeAsync();
        await using var _ = factory;
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        using var aliceApi = factory.CreateClient(alice.Token);
        for (var i = 0; i < 3; i++)
            (await aliceApi.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text = $"m{i}" })).EnsureSuccessStatusCode();
        var laptop = await factory.LoginAsync("bob");
        using var phoneApi = factory.CreateClient(bob.Token);
        using var laptopApi = factory.CreateClient(laptop.Token);

        Assert.Equal(HttpStatusCode.NoContent, (await phoneApi.PostAsJsonAsync("/api/sync/ack", new { pts = 3 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await phoneApi.PostAsJsonAsync("/api/sync/ack", new { pts = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await laptopApi.PostAsJsonAsync("/api/sync/ack", new { pts = 2 })).StatusCode);

        var beyond = await phoneApi.PostAsJsonAsync("/api/sync/ack", new { pts = 4 });
        Assert.Equal(HttpStatusCode.BadRequest, beyond.StatusCode);
        Assert.Contains("INVALID_PTS", await beyond.Content.ReadAsStringAsync());

        await using var connection = new NpgsqlConnection(Db.ConnectionString);
        var acked = (await connection.QueryAsync<long>(
            "SELECT acked_pts FROM user_sync_state WHERE user_id = @bob ORDER BY acked_pts", new { bob = bob.UserId })).ToList();
        Assert.Equal([2L, 3L], acked); // two devices; the phone did not roll back from 3 to 1
    }

    [Fact]
    public async Task Cleanup_DeletesExpiredJournalAndSentEvents_KeepsTheRest()
    {
        var (factory, alice, bob) = await ArrangeAsync();
        await using var _ = factory;
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        using var aliceApi = factory.CreateClient(alice.Token);
        for (var i = 0; i < 3; i++)
            (await aliceApi.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text = $"m{i}" })).EnsureSuccessStatusCode();
        await factory.Services.GetRequiredService<OutboxDispatcher>().DispatchPendingAsync();

        // The first message is older than the journal retention, its event was dispatched long ago.
        await ExecuteAsync("UPDATE user_updates SET created_at = now() - interval '31 days' WHERE pts = 1");
        await ExecuteAsync("UPDATE outbox SET processed_at = now() - interval '8 days' WHERE id = (SELECT MIN(id) FROM outbox)");
        await ExecuteAsync("INSERT INTO outbox (type, payload, created_at) VALUES ('Pending', '{}', now() - interval '60 days')");

        var (updates, events, _) = await factory.Services.GetRequiredService<JournalCleanup>().CleanupAsync();

        Assert.Equal((2, 1), (updates, events)); // one entry each for Alice and Bob; one event
        await using var connection = new NpgsqlConnection(Db.ConnectionString);
        Assert.Equal(4, await connection.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM user_updates"));
        Assert.Equal(1, await connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM outbox WHERE processed_at IS NULL")); // we do not touch what has not been dispatched
    }
}
