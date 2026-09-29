using BasicApi.IntegrationTests.Infrastructure;
using BasicApi.Storage.Repositories;
using Dapper;
using Npgsql;

namespace BasicApi.IntegrationTests.Repositories;

public class PrivateChatTests(PostgresFixture db) : DbTest(db)
{
    private ChatRepository Repository => new(Db.ConnectionFactory);

    [Fact]
    public async Task GetOrCreate_SecondCall_ReturnsSameChat_RegardlessOfOrder()
    {
        var alice = await Data.UserAsync("alice");
        var bob = await Data.UserAsync("bob");

        var first = await Repository.GetOrCreatePrivateChatAsync(alice, bob);
        var second = await Repository.GetOrCreatePrivateChatAsync(bob, alice);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.ChatId, second.ChatId);
        Assert.True(await Repository.IsMemberAsync(first.ChatId, alice));
        Assert.True(await Repository.IsMemberAsync(first.ChatId, bob));
    }

    [Fact]
    public async Task GetOrCreate_ParallelCalls_CreateExactlyOneChat()
    {
        // Двойной клик / два устройства: раньше проверка «есть ли чат» и вставка
        // не были атомарны, и пара получала два личных чата.
        var alice = await Data.UserAsync("alice");
        var bob = await Data.UserAsync("bob");

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            Task.Run(() => i % 2 == 0
                ? Repository.GetOrCreatePrivateChatAsync(alice, bob)
                : Repository.GetOrCreatePrivateChatAsync(bob, alice))));

        Assert.Single(results.Select(r => r.ChatId).Distinct());
        Assert.Single(results, r => r.Created);

        await using var connection = new NpgsqlConnection(Db.ConnectionString);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM chats"));
        Assert.Equal(2, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM chat_members"));
    }
}

/// <summary>
/// Миграция ключа личного чата на базе, где дубли уже успели появиться.
/// </summary>
[Collection(PostgresCollection.Name)]
public class PrivateChatKeyMigrationTests(PostgresFixture db)
{
    [Fact]
    public async Task Migration_MergesExistingDuplicates_KeepingMessagesAndLatestReadPointer()
    {
        var connectionString = await db.CreateEmptyDatabaseAsync("private_key_migration");
        PostgresFixture.WithRunner(connectionString, runner => runner.MigrateUp(5));

        await using var connection = new NpgsqlConnection(connectionString);
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var carol = Guid.NewGuid();
        foreach (var (id, name) in new[] { (alice, "alice"), (bob, "bob"), (carol, "carol") })
        {
            await connection.ExecuteAsync(
                "INSERT INTO users (id, username, email, password_hash, display_name) VALUES (@id, @name, @name || '@t', 'x', @name)",
                new { id, name });
        }

        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        async Task<Guid> Chat(DateTime createdAt, params Guid[] members)
        {
            var chatId = Guid.NewGuid();
            await connection.ExecuteAsync(
                "INSERT INTO chats (id, type, created_at) VALUES (@chatId, 'private', @createdAt)", new { chatId, createdAt });
            foreach (var m in members)
                await connection.ExecuteAsync(
                    "INSERT INTO chat_members (chat_id, user_id) VALUES (@chatId, @m)", new { chatId, m });
            return chatId;
        }
        async Task<Guid> Message(Guid chatId, Guid sender, DateTime at)
        {
            var id = Guid.NewGuid();
            await connection.ExecuteAsync(
                "INSERT INTO messages (id, chat_id, sender_id, text, created_at) VALUES (@id, @chatId, @sender, 'm', @at)",
                new { id, chatId, sender, at });
            return id;
        }

        var keeper = await Chat(t0, alice, bob);                    // самый старый — остаётся
        var dup = await Chat(t0.AddHours(1), bob, alice);           // дубль той же пары
        var other = await Chat(t0, alice, carol);                   // другая пара — не трогаем

        var m1 = await Message(keeper, alice, t0.AddMinutes(1));
        var m2 = await Message(dup, bob, t0.AddHours(2));
        await Message(other, carol, t0.AddMinutes(5));

        // Алиса прочитала дубль дальше, чем основной чат — указатель должен стать самым поздним.
        await connection.ExecuteAsync(
            "UPDATE chat_members SET last_read_message_id = @m1 WHERE chat_id = @keeper AND user_id = @alice",
            new { m1, keeper, alice });
        await connection.ExecuteAsync(
            "UPDATE chat_members SET last_read_message_id = @m2 WHERE chat_id = @dup AND user_id = @alice",
            new { m2, dup, alice });

        PostgresFixture.MigrateUp(connectionString);

        var chats = (await connection.QueryAsync<Guid>("SELECT id FROM chats ORDER BY id")).ToHashSet();
        Assert.Equal(new HashSet<Guid> { keeper, other }, chats);

        var keeperMessages = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM messages WHERE chat_id = @keeper", new { keeper });
        Assert.Equal(2, keeperMessages);

        var alicePointer = await connection.ExecuteScalarAsync<Guid?>(
            "SELECT last_read_message_id FROM chat_members WHERE chat_id = @keeper AND user_id = @alice",
            new { keeper, alice });
        Assert.Equal(m2, alicePointer);

        var keys = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(DISTINCT private_key) FROM chats WHERE type = 'private'");
        Assert.Equal(2, keys);
    }
}
