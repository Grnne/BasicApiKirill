using BasicApi.Storage.Dto;

namespace BasicApi.Storage.Interfaces;

public interface IOutboxRepository
{
    /// <summary>Puts an event into the outbox - within the current transaction, if one is open.</summary>
    Task EnqueueAsync(string type, string payloadJson, CancellationToken ct = default);

    /// <summary>
    /// Undispatched events in order, with row locks held until the end of the transaction
    /// (SKIP LOCKED: a second dispatcher takes different ones). Call within a transaction.
    /// </summary>
    Task<IReadOnlyList<OutboxRow>> LockPendingAsync(int limit, CancellationToken ct = default);

    Task MarkProcessedAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default);

    /// <summary>A failed dispatch attempt; after <paramref name="giveUpAfter"/> the event is dropped.</summary>
    Task<int> MarkFailedAsync(long id, int giveUpAfter, CancellationToken ct = default);

    /// <summary>Deletes dispatched events older than <paramref name="olderThan"/>, in batches.</summary>
    Task<int> DeleteProcessedOlderThanAsync(DateTime olderThan, int batchSize, CancellationToken ct = default);
}
