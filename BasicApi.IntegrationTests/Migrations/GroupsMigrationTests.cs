using BasicApi.IntegrationTests.Infrastructure;
using Dapper;
using Npgsql;

namespace BasicApi.IntegrationTests.Migrations;

[Collection(PostgresCollection.Name)]
public class GroupsMigrationTests(PostgresFixture db)
{
    [Fact]
    public async Task Migration_GivesExistingGroupsAnOwner_AndOnlyOne()
    {
        var connectionString = await db.CreateEmptyDatabaseAsync("groups_migration");
        PostgresFixture.WithRunner(connectionString, runner => runner.MigrateUp(19));

        await using var connection = new NpgsqlConnection(connectionString);
        var users = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        foreach (var (id, i) in users.Select((id, i) => (id, i)))
        {
            await connection.ExecuteAsync(
                "INSERT INTO users (id, username, email, password_hash, display_name) VALUES (@id, @name, @name || '@t', 'x', @name)",
                new { id, name = $"u{i}" });
        }

        var group = Guid.NewGuid();
        var privateChat = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO chats (id, title, type) VALUES (@group, 'g', 'group'), (@privateChat, NULL, 'private')",
            new { group, privateChat });
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        // Joined in the order 1, 0, 2: user 1 is the earliest.
        await connection.ExecuteAsync(@"
            INSERT INTO chat_members (chat_id, user_id, joined_at) VALUES
                (@group, @u1, @t0), (@group, @u0, @t1), (@group, @u2, @t2),
                (@privateChat, @u0, @t0), (@privateChat, @u1, @t0)",
            new { group, privateChat, u0 = users[0], u1 = users[1], u2 = users[2], t0, t1 = t0.AddMinutes(1), t2 = t0.AddMinutes(2) });

        PostgresFixture.WithRunner(connectionString, runner => runner.MigrateUp(20));

        var roles = (await connection.QueryAsync<(Guid ChatId, Guid UserId, string Role)>(
            "SELECT chat_id, user_id, role FROM chat_members")).ToList();
        Assert.Equal(users[1], Assert.Single(roles, r => r.Role == "owner").UserId);
        Assert.All(roles.Where(r => r.ChatId == privateChat), r => Assert.Equal("member", r.Role));

        // A second owner in one chat is refused by the database.
        await Assert.ThrowsAsync<PostgresException>(() => connection.ExecuteAsync(
            "UPDATE chat_members SET role = 'owner' WHERE chat_id = @group AND user_id = @u0", new { group, u0 = users[0] }));
    }
}
