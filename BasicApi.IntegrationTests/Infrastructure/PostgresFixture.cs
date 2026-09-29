using BasicApi.Storage;
using BasicApi.Storage.Interfaces;
using BasicApi.Storage.Migrations;
using BasicApi.Storage.Services;
using Dapper;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace BasicApi.IntegrationTests.Infrastructure;

/// <summary>
/// One Postgres container for the whole run. The schema is created by the same
/// migrations as in production, so the tests also verify that migrations apply from scratch.
/// Requires a running Docker.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    // The same major version as in docker-compose.prod.yml.
    public const string Image = "postgres:17-alpine";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image)
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public IDbConnectionFactory ConnectionFactory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionFactory = new NpgsqlConnectionFactory(ConnectionString);
        MigrateUp(ConnectionString);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>Applies all migrations to the given database.</summary>
    public static void MigrateUp(string connectionString) =>
        WithRunner(connectionString, runner => runner.MigrateUp());

    public static void WithRunner(string connectionString, Action<IMigrationRunner> action)
    {
        using var provider = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddPostgres()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(InitialCreate).Assembly).For.Migrations())
            .BuildServiceProvider(validateScopes: false);

        using var scope = provider.CreateScope();
        action(scope.ServiceProvider.GetRequiredService<IMigrationRunner>());
    }

    /// <summary>Creates an empty database in the same container and returns a connection string to it.</summary>
    public async Task<string> CreateEmptyDatabaseAsync(string name)
    {
        await using (var connection = new NpgsqlConnection(ConnectionString))
        {
            await connection.ExecuteAsync($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
            await connection.ExecuteAsync($"CREATE DATABASE \"{name}\"");
        }

        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ToString();
    }

    /// <summary>Deletes all data; the schema stays.</summary>
    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.ExecuteAsync(
            "TRUNCATE outbox, user_sync_state, user_updates, user_pts, sessions, messages, chat_members, chats, users RESTART IDENTITY CASCADE");
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}

/// <summary>
/// Base class: every test starts with an empty database. All tests sharing the database
/// live in one collection, so xUnit runs them sequentially.
/// </summary>
[Collection(PostgresCollection.Name)]
public abstract class DbTest(PostgresFixture db) : IAsyncLifetime
{
    protected PostgresFixture Db { get; } = db;
    protected TestData Data { get; } = new(db.ConnectionFactory);

    /// <summary>A separate session per call, like a separate request to the API.</summary>
    protected DbSession NewSession() => new(Db.ConnectionFactory);

    public virtual Task InitializeAsync() => Db.ResetAsync();

    public virtual Task DisposeAsync() => Task.CompletedTask;
}
