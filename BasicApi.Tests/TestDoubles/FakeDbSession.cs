using System.Data;
using BasicApi.Storage;

namespace BasicApi.Tests.TestDoubles;

/// <summary>
/// A session without a database: a transaction simply runs the work, and "commit" is its
/// successful completion. Direct queries are not supported: in unit tests they are made by
/// repository mocks.
/// </summary>
public sealed class FakeDbSession : IDbSession
{
    private readonly List<Action> _onCommitted = [];

    public bool InTransaction { get; private set; }
    public int Commits { get; private set; }
    public int Rollbacks { get; private set; }

    public void OnCommitted(Action action)
    {
        if (InTransaction) _onCommitted.Add(action);
        else action();
    }

    public async Task<T> InTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work, IsolationLevel isolation = IsolationLevel.ReadCommitted, CancellationToken ct = default)
    {
        if (InTransaction)
            return await work(ct);

        InTransaction = true;
        try
        {
            var result = await work(ct);
            Commits++;
            InTransaction = false;
            foreach (var action in _onCommitted) action();
            return result;
        }
        catch
        {
            Rollbacks++;
            throw;
        }
        finally
        {
            InTransaction = false;
            _onCommitted.Clear();
        }
    }

    public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? param = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<T> QuerySingleAsync<T>(string sql, object? param = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<int> ExecuteAsync(string sql, object? param = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<T?> ExecuteScalarAsync<T>(string sql, object? param = null, CancellationToken ct = default) => throw new NotSupportedException();
}
