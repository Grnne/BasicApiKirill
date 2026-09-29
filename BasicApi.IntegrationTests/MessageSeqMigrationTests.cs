using BasicApi.IntegrationTests.Infrastructure;
using Dapper;
using Npgsql;

namespace BasicApi.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class MessageSeqMigrationTests(PostgresFixture db)
{
    [Fact]
    public async Task Migration_NumbersExistingMessages_AndKeepsReadPointers()
    {
        var connectionString = await db.CreateEmptyDatabaseAsync("message_seq_migration");
        PostgresFixture.WithRunner(connectionString, runner => runner.MigrateUp(8));

        await using var connection = new NpgsqlConnection(connectionString);
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        foreach (var (id, name) in new[] { (alice, "alice"), (bob, "bob") })
        {
            await connection.ExecuteAsync(
                "INSERT INTO users (id, username, email, password_hash, display_name) VALUES (@id, @name, @name || '@t', 'x', @name)",
                new { id, name });
        }

        var chat = Guid.NewGuid();
        var empty = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO chats (id, type) VALUES (@chat, 'private'), (@empty, 'private')", new { chat, empty });
        await connection.ExecuteAsync(@"
            INSERT INTO chat_members (chat_id, user_id) VALUES (@chat, @alice), (@chat, @bob), (@empty, @alice)",
            new { chat, empty, alice, bob });

        // Вставляем не по порядку времени; два сообщения — с одинаковым временем.
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        // Порядок uuid в Postgres и Guid в .NET различается — берём id, где они совпадают.
        var ids = Enumerable.Range(1, 4).Select(i => Guid.Parse($"00000000-0000-0000-0000-00000000000{i}")).ToArray();
        var rows = new[] { (ids[3], t0.AddMinutes(2)), (ids[0], t0), (ids[2], t0.AddMinutes(1)), (ids[1], t0.AddMinutes(1)) };
        foreach (var (id, at) in rows)
        {
            await connection.ExecuteAsync(
                "INSERT INTO messages (id, chat_id, sender_id, text, created_at) VALUES (@id, @chat, @bob, 'm', @at)",
                new { id, chat, bob, at });
        }
        await connection.ExecuteAsync(
            "UPDATE chat_members SET last_read_message_id = @read WHERE chat_id = @chat AND user_id = @alice",
            new { read = ids[2], chat, alice });

        PostgresFixture.WithRunner(connectionString, runner => runner.MigrateUp(9));

        // Порядок — по (created_at, id), как отдавала пагинация до миграции.
        var seqs = (await connection.QueryAsync<Guid>(
            "SELECT id FROM messages WHERE chat_id = @chat ORDER BY seq", new { chat })).ToArray();
        Assert.Equal([ids[0], ids[1], ids[2], ids[3]], seqs);

        Assert.Equal(4, await connection.ExecuteScalarAsync<long>("SELECT last_seq FROM chats WHERE id = @chat", new { chat }));
        Assert.Equal(0, await connection.ExecuteScalarAsync<long>("SELECT last_seq FROM chats WHERE id = @empty", new { empty }));

        // Алиса прочитала до третьего сообщения; Боб — ничего.
        Assert.Equal(3, await connection.ExecuteScalarAsync<long>(
            "SELECT last_read_seq FROM chat_members WHERE chat_id = @chat AND user_id = @alice", new { chat, alice }));
        Assert.Equal(0, await connection.ExecuteScalarAsync<long>(
            "SELECT last_read_seq FROM chat_members WHERE chat_id = @chat AND user_id = @bob", new { chat, bob }));

        // Откат возвращает указатель на то же сообщение.
        PostgresFixture.WithRunner(connectionString, runner => runner.MigrateDown(8));
        Assert.Equal(ids[2], await connection.ExecuteScalarAsync<Guid>(
            "SELECT last_read_message_id FROM chat_members WHERE chat_id = @chat AND user_id = @alice", new { chat, alice }));
    }
}
