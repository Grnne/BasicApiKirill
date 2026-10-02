using BasicApi.Extensions;
using BasicApi.IntegrationTests.Infrastructure;
using Dapper;
using FluentMigrator;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BasicApi.IntegrationTests.Migrations;

[Collection(PostgresCollection.Name)]
public class MigrationTimeoutTests(PostgresFixture db)
{
    [Fact]
    public async Task LongMigration_IsNotCutByTheRequestsCommandTimeout()
    {
        // Found by the review: the runner took the command timeout of the connection string, meant
        // for requests. A migration longer than that (an index on a big table) was rolled back and
        // the app failed to start, again at every restart.
        var connectionString = new NpgsqlConnectionStringBuilder(await db.CreateEmptyDatabaseAsync("migrations_slow"))
        {
            CommandTimeout = 1
        }.ToString();

        using (var provider = new ServiceCollection()
                   .AddFluentMigratorCore()
                   .ConfigureRunner(rb => rb.AddAppMigrations(connectionString)
                       .ScanIn(typeof(SlowMigration).Assembly).For.Migrations())
                   .BuildServiceProvider(validateScopes: false))
        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
        }

        await using var connection = new NpgsqlConnection(connectionString);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM \"VersionInfo\" WHERE \"Version\" = @version", new { version = SlowMigration.Version }));
    }

    /// <summary>Longer than the command timeout of the test's connection string.</summary>
    [Migration(Version)]
    public sealed class SlowMigration : Migration
    {
        public const long Version = 900_001;

        public override void Up() => Execute.Sql("SELECT pg_sleep(2)");

        public override void Down() { }
    }
}
