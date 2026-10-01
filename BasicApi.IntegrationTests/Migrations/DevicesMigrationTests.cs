using BasicApi.IntegrationTests.Infrastructure;
using Dapper;
using Npgsql;

namespace BasicApi.IntegrationTests.Migrations;

[Collection(PostgresCollection.Name)]
public class DevicesMigrationTests(PostgresFixture db)
{
    [Fact]
    public async Task Migration_GivesOpenSignInsTheirDevice()
    {
        var connectionString = await db.CreateEmptyDatabaseAsync("devices_migration");
        PostgresFixture.WithRunner(connectionString, runner => runner.MigrateUp(28));

        await using var connection = new NpgsqlConnection(connectionString);
        var user = Guid.NewGuid();
        await connection.ExecuteAsync(
            "INSERT INTO users (id, username, email, password_hash, display_name) VALUES (@user, 'u', 'u@t', 'x', 'u')",
            new { user });
        Guid live = Guid.NewGuid(), ended = Guid.NewGuid();
        var signedIn = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        // The live sign-in was rotated once; the other one was logged out.
        await connection.ExecuteAsync(@"
            INSERT INTO sessions (id, user_id, family_id, refresh_token_hash, created_at, expires_at, revoked_at) VALUES
                (gen_random_uuid(), @user, @live, 'a', @signedIn, now() + interval '1 day', @rotated),
                (gen_random_uuid(), @user, @live, 'b', @rotated, now() + interval '1 day', NULL),
                (gen_random_uuid(), @user, @ended, 'c', @signedIn, now() + interval '1 day', @rotated)",
            new { user, live, ended, signedIn, rotated = signedIn.AddHours(1) });

        PostgresFixture.WithRunner(connectionString, runner => runner.MigrateUp(29));

        var device = Assert.Single(await connection.QueryAsync<(Guid Id, Guid UserId, DateTime CreatedAt)>(
            "SELECT id, user_id, created_at FROM devices"));
        Assert.Equal((live, user, signedIn), (device.Id, device.UserId, device.CreatedAt.ToUniversalTime()));
    }
}
