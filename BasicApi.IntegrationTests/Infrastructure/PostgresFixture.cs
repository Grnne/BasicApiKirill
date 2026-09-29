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
/// Один контейнер Postgres на весь прогон. Схема создаётся теми же миграциями,
/// что и в проде, — поэтому тесты заодно проверяют, что миграции применяются с нуля.
/// Требуется запущенный Docker.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    // Та же мажорная версия, что в docker-compose.prod.yml.
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

    /// <summary>Применяет все миграции к указанной базе.</summary>
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

    /// <summary>Создаёт пустую базу в том же контейнере и возвращает строку подключения к ней.</summary>
    public async Task<string> CreateEmptyDatabaseAsync(string name)
    {
        await using (var connection = new NpgsqlConnection(ConnectionString))
        {
            await connection.ExecuteAsync($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
            await connection.ExecuteAsync($"CREATE DATABASE \"{name}\"");
        }

        return new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ToString();
    }

    /// <summary>Удаляет все данные, схема остаётся.</summary>
    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.ExecuteAsync(
            "TRUNCATE sessions, messages, chat_members, chats, users RESTART IDENTITY CASCADE");
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}

/// <summary>
/// Базовый класс: каждый тест начинается с пустой базы. Все тесты с общей базой
/// живут в одной коллекции, поэтому xUnit гоняет их последовательно.
/// </summary>
[Collection(PostgresCollection.Name)]
public abstract class DbTest(PostgresFixture db) : IAsyncLifetime
{
    protected PostgresFixture Db { get; } = db;
    protected TestData Data { get; } = new(db.ConnectionFactory);

    public virtual Task InitializeAsync() => Db.ResetAsync();

    public virtual Task DisposeAsync() => Task.CompletedTask;
}
