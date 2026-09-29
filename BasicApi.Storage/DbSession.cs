using System.Data;
using System.Data.Common;
using BasicApi.Storage.Interfaces;
using Dapper;

namespace BasicApi.Storage;

/// <summary>
/// Доступ к базе для репозиториев одного запроса (или вызова хаба).
/// Вне транзакции каждый запрос берёт соединение из пула и сразу возвращает его —
/// поэтому независимые запросы можно запускать параллельно. Внутри
/// <see cref="InTransactionAsync{T}"/> все запросы всех репозиториев идут в одной
/// транзакции на одном соединении: так доменное изменение и его событие (outbox)
/// фиксируются вместе. Параллельных запросов внутри транзакции быть не должно.
/// </summary>
public interface IDbSession
{
    bool InTransaction { get; }

    Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? param = null, CancellationToken ct = default);
    Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null, CancellationToken ct = default);
    Task<T> QuerySingleAsync<T>(string sql, object? param = null, CancellationToken ct = default);
    Task<int> ExecuteAsync(string sql, object? param = null, CancellationToken ct = default);
    Task<T?> ExecuteScalarAsync<T>(string sql, object? param = null, CancellationToken ct = default);

    /// <summary>
    /// Выполняет работу в транзакции: успех — коммит, исключение — откат.
    /// Вложенный вызов присоединяется к уже открытой транзакции.
    /// </summary>
    Task<T> InTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken ct = default);
}

public sealed class DbSession(IDbConnectionFactory connectionFactory) : IDbSession, IAsyncDisposable
{
    private DbConnection? _connection;
    private DbTransaction? _transaction;

    public bool InTransaction => _transaction is not null;

    public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? param = null, CancellationToken ct = default) =>
        RunAsync(async (c, d) => (IReadOnlyList<T>)(await c.QueryAsync<T>(d)).AsList(), sql, param, ct);

    public Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null, CancellationToken ct = default) =>
        RunAsync((c, d) => c.QueryFirstOrDefaultAsync<T>(d), sql, param, ct);

    public Task<T> QuerySingleAsync<T>(string sql, object? param = null, CancellationToken ct = default) =>
        RunAsync((c, d) => c.QuerySingleAsync<T>(d), sql, param, ct);

    public Task<int> ExecuteAsync(string sql, object? param = null, CancellationToken ct = default) =>
        RunAsync((c, d) => c.ExecuteAsync(d), sql, param, ct);

    public Task<T?> ExecuteScalarAsync<T>(string sql, object? param = null, CancellationToken ct = default) =>
        RunAsync((c, d) => c.ExecuteScalarAsync<T>(d), sql, param, ct);

    private async Task<T> RunAsync<T>(
        Func<IDbConnection, CommandDefinition, Task<T>> query, string sql, object? param, CancellationToken ct)
    {
        if (_transaction is not null)
            return await query(_connection!, new CommandDefinition(sql, param, _transaction, cancellationToken: ct));

        // Dapper сам откроет и закроет соединение; в пул оно вернётся при Dispose.
        using var connection = connectionFactory.CreateConnection();
        return await query(connection, new CommandDefinition(sql, param, cancellationToken: ct));
    }

    public async Task<T> InTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken ct = default)
    {
        if (_transaction is not null)
            return await work(ct);

        _connection = (DbConnection)connectionFactory.CreateConnection();
        try
        {
            await _connection.OpenAsync(ct);
            _transaction = await _connection.BeginTransactionAsync(isolation, ct);

            var result = await work(ct);

            // Коммит не отменяем: запрос мог оборваться уже после того, как работа
            // сделана, и откатывать её из-за ушедшего клиента нельзя.
            await _transaction.CommitAsync(CancellationToken.None);
            return result;
        }
        finally
        {
            // Без коммита Dispose транзакции откатывает её.
            if (_transaction is not null)
                await _transaction.DisposeAsync();
            await _connection.DisposeAsync();
            _transaction = null;
            _connection = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
            await _transaction.DisposeAsync();
        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}
