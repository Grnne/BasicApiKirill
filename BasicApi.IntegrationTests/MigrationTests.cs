using BasicApi.IntegrationTests.Infrastructure;
using BasicApi.Storage.Migrations;
using Dapper;
using FluentMigrator;
using Npgsql;

namespace BasicApi.IntegrationTests;

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
        // Откат нужен при неудачном деплое: Down() каждой миграции должен работать.
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
}
