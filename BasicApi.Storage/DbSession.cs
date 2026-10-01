using System.Data;
using System.Data.Common;
using BasicApi.Storage.Interfaces;
using Dapper;
using Npgsql;

namespace BasicApi.Storage;

/// <summary>
/// Database access for one request's repositories: each query uses its own pooled connection, except inside
/// <see cref="InTransactionAsync{T}"/>, where all share one transaction. No parallel queries inside it.
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
    /// An action after the current transaction commits (not run on rollback);
    /// outside a transaction — immediately. For example, to wake up the event dispatcher: before the commit
    /// it would not see the events yet.
    /// </summary>
    void OnCommitted(Action action);

    /// <summary>
    /// Runs work in a transaction: success — commit, exception — rollback.
    /// A nested call joins the already open transaction.
    /// Postgres may end a transaction to break a deadlock (two requests writing to the journals of
    /// the same people in different orders) or on a serialization failure: then the work is run
    /// again from the start in a new transaction, so it must touch nothing but the database before
    /// the commit (other effects go through <see cref="OnCommitted"/>). Work that cannot be
    /// repeated passes <paramref name="retryOnConflict"/> false.
    /// </summary>
    Task<T> InTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken ct = default,
        bool retryOnConflict = true);
}

public sealed class DbSession(IDbConnectionFactory connectionFactory) : IDbSession, IAsyncDisposable
{
    private DbConnection? _connection;
    private DbTransaction? _transaction;
    private readonly List<Action> _onCommitted = [];

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

        // Dapper opens and closes the connection itself; it goes back to the pool on Dispose.
        using var connection = connectionFactory.CreateConnection();
        return await query(connection, new CommandDefinition(sql, param, cancellationToken: ct));
    }

    public void OnCommitted(Action action)
    {
        if (_transaction is null)
            action();
        else
            _onCommitted.Add(action);
    }

    /// <summary>Attempts of a transaction Postgres ended on a conflict: rare, and each one is a request's worth.</summary>
    private const int MaxAttempts = 3;

    public async Task<T> InTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work,
        IsolationLevel isolation = IsolationLevel.ReadCommitted,
        CancellationToken ct = default,
        bool retryOnConflict = true)
    {
        if (_transaction is not null)
            return await work(ct);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await RunInTransactionAsync(work, isolation, ct);
            }
            catch (Exception e) when (retryOnConflict && attempt < MaxAttempts && IsConflict(e))
            {
                // A short pause, different each time: the other transaction finishes meanwhile.
                await Task.Delay(Random.Shared.Next(5, 25) * attempt, ct);
            }
        }
    }

    /// <summary>Deadlock or serialization failure: nothing wrong with the work, only its timing.</summary>
    private static bool IsConflict(Exception? e)
    {
        for (; e is not null; e = e.InnerException)
        {
            if (e is PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure })
                return true;
        }
        return false;
    }

    private async Task<T> RunInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work, IsolationLevel isolation, CancellationToken ct)
    {
        _connection = (DbConnection)connectionFactory.CreateConnection();
        Action[]? committed = null;
        try
        {
            await _connection.OpenAsync(ct);
            _transaction = await _connection.BeginTransactionAsync(isolation, ct);

            var result = await work(ct);

            // We do not cancel the commit: the request may have been aborted after the work
            // was already done, and it must not be rolled back because the client left.
            await _transaction.CommitAsync(CancellationToken.None);
            committed = [.. _onCommitted];
            return result;
        }
        finally
        {
            // Without a commit, disposing the transaction rolls it back.
            if (_transaction is not null)
                await _transaction.DisposeAsync();
            await _connection.DisposeAsync();
            _transaction = null;
            _connection = null;
            _onCommitted.Clear();

            foreach (var action in committed ?? [])
                action();
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
