using BasicApi.Storage.Dto;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Storage.Repositories;

public sealed class OutboxRepository(IDbSession db) : IOutboxRepository
{
    public Task EnqueueAsync(string type, string payloadJson, CancellationToken ct = default) =>
        db.ExecuteAsync(
            "INSERT INTO outbox (type, payload) VALUES (@type, @payloadJson::jsonb)", new { type, payloadJson }, ct);

    public Task<IReadOnlyList<OutboxRow>> LockPendingAsync(int limit, CancellationToken ct = default) =>
        db.QueryAsync<OutboxRow>(@"
            SELECT id AS Id, type AS Type, payload::text AS Payload, attempts AS Attempts
            FROM outbox
            WHERE processed_at IS NULL
            ORDER BY id
            LIMIT @limit
            FOR UPDATE SKIP LOCKED", new { limit }, ct);

    public Task MarkProcessedAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default) =>
        ids.Count == 0
            ? Task.CompletedTask
            : db.ExecuteAsync("UPDATE outbox SET processed_at = now() WHERE id = ANY(@ids)", new { ids = ids.ToArray() }, ct);

    public Task<int> MarkFailedAsync(long id, int giveUpAfter, CancellationToken ct = default) =>
        db.QuerySingleAsync<int>(@"
            UPDATE outbox
            SET attempts = attempts + 1,
                processed_at = CASE WHEN attempts + 1 >= @giveUpAfter THEN now() END
            WHERE id = @id
            RETURNING attempts", new { id, giveUpAfter }, ct);

    public Task<int> DeleteProcessedOlderThanAsync(DateTime olderThan, int batchSize, CancellationToken ct = default) =>
        db.ExecuteAsync(@"
            DELETE FROM outbox
            WHERE id IN (SELECT id FROM outbox WHERE processed_at < @olderThan ORDER BY id LIMIT @batchSize)",
            new { olderThan, batchSize }, ct);
}
