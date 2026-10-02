using BasicApi.Storage.Migrations;
using FluentMigrator.Runner;

namespace BasicApi.Extensions;

/// <summary>How the app runs its migrations; the tests run them the same way.</summary>
public static class MigrationSetup
{
    /// <summary>
    /// How long one migration statement may run. The connection string's command timeout is for
    /// requests (30 s); an index on a big table takes minutes, and a migration cut short is rolled
    /// back and stops the app at every start.
    /// </summary>
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromHours(1);

    public static IMigrationRunnerBuilder AddAppMigrations(this IMigrationRunnerBuilder runner, string connectionString) =>
        runner.AddPostgres()
            .WithGlobalConnectionString(connectionString)
            .WithGlobalCommandTimeout(CommandTimeout)
            .ScanIn(typeof(InitialCreate).Assembly).For.Migrations();
}
