using BasicApi.IntegrationTests.Infrastructure;
using BasicApi.Storage.Repositories;

namespace BasicApi.IntegrationTests.Repositories;

/// <summary>Journal writes of concurrent requests to the same people.</summary>
public class JournalConcurrencyTests(PostgresFixture db) : DbTest(db)
{
    [Fact]
    public async Task Transactions_ThatLockJournalsInOppositeOrder_BothCommit()
    {
        // Found by the UI e2e: a send and a reaction in one chat got "deadlock detected" and 500.
        // A transaction writes to the journal more than once (one user, then everyone), so two of
        // them take the journal rows in different orders; Postgres ends one, and it must be redone.
        var a = await Data.UserAsync("alice");
        var b = await Data.UserAsync("bob");
        // The order the database sorts them in, the order one journal write locks them.
        var low = await NewSession().ExecuteScalarAsync<Guid>(
            "SELECT u FROM unnest(@ids) AS u ORDER BY u LIMIT 1", new { ids = new[] { a, b } });
        var high = low == a ? b : a;

        await using var first = NewSession();
        await using var second = NewSession();
        var firstHoldsHigh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var one = first.InTransactionAsync(async ct =>
        {
            var journal = new UpdateJournalRepository(first);
            await journal.AppendAsync([high], "T", "{}", ct);
            firstHoldsHigh.TrySetResult();
            // The second one takes `low` and waits for `high` meanwhile; then this one waits for `low`.
            await WaitForLockWaitAsync(ct);
            await journal.AppendAsync([low], "T", "{}", ct);
            return 0;
        });
        var two = Task.Run(async () =>
        {
            await firstHoldsHigh.Task;
            return await second.InTransactionAsync(async ct =>
            {
                await new UpdateJournalRepository(second).AppendAsync([low, high], "T", "{}", ct);
                return 0;
            });
        });

        await Task.WhenAll(one, two);

        var journal = new UpdateJournalRepository(NewSession());
        Assert.Equal(2, await journal.GetPtsAsync(low));
        Assert.Equal(2, await journal.GetPtsAsync(high));
        Assert.Equal(2, (await journal.GetSinceAsync(low, 0, 10)).Count);
    }

    private async Task WaitForLockWaitAsync(CancellationToken ct)
    {
        var observer = NewSession();
        for (var i = 0; i < 100; i++)
        {
            var waiting = await observer.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND datname = current_database()",
                ct: ct);
            if (waiting > 0)
                return;
            await Task.Delay(50, ct);
        }
        Assert.Fail("The second transaction never waited for a lock.");
    }
}
