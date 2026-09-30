using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;
using Dapper;
using Npgsql;

namespace BasicApi.IntegrationTests.Api;

/// <summary>The chat list in pages, by chats.last_activity_at (plan 2, F3.6).</summary>
public class ChatListPagingTests(PostgresFixture db) : DbTest(db)
{
    private static Dictionary<string, string?> Settings => new() { ["RateLimiting:CommandsPer10Seconds"] = "1000" };

    private static async Task<List<Guid>> AllPagesAsync(HttpClient api, int limit)
    {
        var ids = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = await api.GetJsonAsync($"/api/chats/page?limit={limit}{(cursor is null ? "" : $"&cursor={cursor}")}");
            var items = page.GetProperty("items").EnumerateArray().ToList();
            Assert.True(items.Count <= limit);
            ids.AddRange(items.Select(i => i.Id("chatId")));
            cursor = page.GetProperty("nextCursor").GetString();
            Assert.Equal(cursor is not null, page.GetProperty("hasMore").GetBoolean());
        } while (cursor is not null);
        return ids;
    }

    [Fact]
    public async Task Pages_CoverEveryChatOnce_InTheOrderOfTheFullList()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        using var api = factory.CreateClient(alice.Token);
        var chats = new List<Guid>();
        for (var i = 0; i < 7; i++)
        {
            var other = await Data.UserAsync($"u{i}");
            // Three chats share one moment: the order among them is by id, stable across pages.
            chats.Add(await Data.PrivateChatAsync(alice.UserId, other, TestData.T0.AddMinutes(i < 3 ? 0 : i)));
        }
        await Data.MessageAsync(chats[1], alice.UserId, "revived", TestData.T0.AddHours(1));

        var paged = await AllPagesAsync(api, 3);

        var full = (await api.GetJsonAsync("/api/chats")).EnumerateArray().Select(c => c.Id("chatId")).ToList();
        Assert.Equal(full, paged);
        Assert.Equal(7, paged.Distinct().Count());
        Assert.Equal(chats[1], paged[0]);
        Assert.Equal(TestData.T0.AddHours(1), (await api.GetJsonAsync("/api/chats/page?limit=1"))
            .GetProperty("items")[0].GetProperty("lastActivityAt").GetDateTime().ToUniversalTime());
    }

    [Fact]
    public async Task Activity_MovesWithEverySend_AndStaysWhenTheLastMessageIsDeleted()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var api = factory.CreateClient(alice.Token);
        var older = await Data.PrivateChatAsync(alice.UserId, bob.UserId, TestData.T0);
        var group = (await api.PostJsonAsync("/api/chats/groups", new { title = "team", memberIds = new[] { bob.UserId } })).Id("chatId");

        var sent = (await api.PostJsonAsync($"/api/chats/{older}/messages", new { text = "now" })).Id();
        Assert.Equal([older, group], await AllPagesAsync(api, 10));

        // Deleting it does not send the chat back down.
        (await api.DeleteAsync($"/api/chats/{older}/messages/{sent}?forEveryone=true")).EnsureSuccessStatusCode();
        Assert.Equal([older, group], await AllPagesAsync(api, 10));

        // A system message is activity too.
        (await api.PatchAsJsonAsync($"/api/chats/{group}", new { title = "renamed" })).EnsureSuccessStatusCode();
        Assert.Equal([group, older], await AllPagesAsync(api, 10));
    }

    [Fact]
    public async Task BrokenCursor_Is400()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        using var api = factory.CreateClient(alice.Token);

        foreach (var cursor in new[] { "abc", "AQ", Convert.ToBase64String(new byte[25]) })
        {
            var response = await api.GetAsync($"/api/chats/page?cursor={Uri.EscapeDataString(cursor)}");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("INVALID_CURSOR", await response.ErrorCodeAsync());
        }
    }
}

[Collection(PostgresCollection.Name)]
public class ChatLastActivityMigrationTests(PostgresFixture db)
{
    [Fact]
    public async Task Migration_TakesTheLastLiveMessage_OrTheCreationTime()
    {
        var connectionString = await db.CreateEmptyDatabaseAsync("last_activity_migration");
        PostgresFixture.WithRunner(connectionString, runner => runner.MigrateUp(20));

        await using var connection = new NpgsqlConnection(connectionString);
        var user = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO users (id, username, email, password_hash, display_name) VALUES (@user, 'u', 'u@t', 'x', 'u')", new { user });
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var (active, empty) = (Guid.NewGuid(), Guid.NewGuid());
        await connection.ExecuteAsync(
            "INSERT INTO chats (id, type, created_at) VALUES (@active, 'private', @t0), (@empty, 'private', @t0)", new { active, empty, t0 });
        await connection.ExecuteAsync(@"
            INSERT INTO messages (id, chat_id, sender_id, text, created_at, seq, deleted_at) VALUES
                (gen_random_uuid(), @active, @user, 'kept', @t1, 1, NULL),
                (gen_random_uuid(), @active, @user, '', @t2, 2, @t2)",
            new { active, user, t1 = t0.AddHours(1), t2 = t0.AddHours(2) });

        PostgresFixture.WithRunner(connectionString, runner => runner.MigrateUp(21));

        var activity = (await connection.QueryAsync<(Guid Id, DateTime At)>("SELECT id, last_activity_at FROM chats"))
            .ToDictionary(r => r.Id, r => r.At.ToUniversalTime());
        Assert.Equal(t0.AddHours(1), activity[active]);
        Assert.Equal(t0, activity[empty]);
    }
}
