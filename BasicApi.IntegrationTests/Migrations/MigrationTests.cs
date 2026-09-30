using BasicApi.IntegrationTests.Infrastructure;
using BasicApi.Storage.Migrations;
using Dapper;
using FluentMigrator;
using Npgsql;

namespace BasicApi.IntegrationTests.Migrations;

[Collection(PostgresCollection.Name)]
public class MigrationTests(PostgresFixture db)
{
    private static readonly long[] AllVersions = typeof(InitialCreate).Assembly.GetTypes()
        .Select(t => t.GetCustomAttributes(typeof(MigrationAttribute), false).Cast<MigrationAttribute>().FirstOrDefault())
        .Where(a => a is not null)
        .Select(a => a!.Version)
        .Order()
        .ToArray();

    [Fact]
    public async Task MigrateUp_OnEmptyDatabase_AppliesAllMigrations()
    {
        var connectionString = await db.CreateEmptyDatabaseAsync("migrations_up");

        PostgresFixture.MigrateUp(connectionString);

        await using var connection = new NpgsqlConnection(connectionString);
        var applied = (await connection.QueryAsync<long>(
            "SELECT \"Version\" FROM \"VersionInfo\" ORDER BY \"Version\"")).ToArray();
        Assert.Equal(AllVersions, applied);

        var tables = (await connection.QueryAsync<string>(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'")).ToHashSet();
        Assert.Superset(new HashSet<string> { "users", "chats", "chat_members", "messages", "sessions" }, tables);
    }

    [Fact]
    public async Task MigrateDownToZero_ThenUp_Succeeds()
    {
        // Rollback is needed after a failed deploy: Down() of every migration must work.
        var connectionString = await db.CreateEmptyDatabaseAsync("migrations_roundtrip");
        PostgresFixture.MigrateUp(connectionString);

        PostgresFixture.WithRunner(connectionString, runner => runner.MigrateDown(0));

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            var remaining = await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(*) FROM information_schema.tables
                WHERE table_schema = 'public' AND table_name <> 'VersionInfo'");
            Assert.Equal(0, remaining);
        }

        PostgresFixture.MigrateUp(connectionString);
    }

    [Theory]
    [InlineData("chat_members", "ix_chat_members_user_id")]
    public async Task Schema_HasIndex(string table, string index)
    {
        // "All chats of a user" is the most frequent query; the PK (chat_id, user_id)
        // does not help with user_id - without a separate index it is a full scan.
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        var exists = await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE tablename = @table AND indexname = @index)",
            new { table, index });

        Assert.True(exists, $"{table}.{index} is missing");
    }

    [Fact]
    public async Task Schema_HasNoTimestampsWithoutTimeZone()
    {
        // timestamp without zone depends on the database session time zone (see migration 8).
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        var columns = await connection.QueryAsync<string>(@"
            SELECT table_name || '.' || column_name FROM information_schema.columns
            WHERE table_schema = 'public' AND data_type = 'timestamp without time zone'
              AND table_name <> 'VersionInfo'");

        Assert.Empty(columns);
    }
}
