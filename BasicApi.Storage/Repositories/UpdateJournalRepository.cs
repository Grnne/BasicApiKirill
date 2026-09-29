using BasicApi.Storage.Dto;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Storage.Repositories;

public sealed class UpdateJournalRepository(IDbSession db) : IUpdateJournal
{
    public Task AppendAsync(IReadOnlyCollection<Guid> userIds, string type, string payloadJson, CancellationToken ct = default)
    {
        if (userIds.Count == 0)
            return Task.CompletedTask;

        // Counters are taken in user_id order: two transactions writing to the journals of the same
        // people lock their rows in the same order and do not wait on each other in a cycle.
        return db.ExecuteAsync(@"
            WITH pts AS (
                INSERT INTO user_pts (user_id, last_pts)
                SELECT u, 1 FROM unnest(@userIds) AS u ORDER BY u
                ON CONFLICT (user_id) DO UPDATE SET last_pts = user_pts.last_pts + 1
                RETURNING user_id, last_pts
            )
            INSERT INTO user_updates (user_id, pts, type, payload)
            SELECT user_id, last_pts, @type, @payloadJson::jsonb FROM pts",
            new { userIds = userIds.Distinct().ToArray(), type, payloadJson }, ct);
    }

    public async Task<long> GetPtsAsync(Guid userId, CancellationToken ct = default) =>
        await db.ExecuteScalarAsync<long?>("SELECT last_pts FROM user_pts WHERE user_id = @userId", new { userId }, ct) ?? 0;

    public Task<IReadOnlyList<UserUpdate>> GetSinceAsync(Guid userId, long sincePts, int limit, CancellationToken ct = default) =>
        db.QueryAsync<UserUpdate>(@"
            SELECT pts AS Pts, type AS Type, payload::text AS Payload, created_at AS CreatedAt
            FROM user_updates
            WHERE user_id = @userId AND pts > @sincePts
            ORDER BY pts
            LIMIT @limit", new { userId, sincePts, limit }, ct);

    public Task AckAsync(Guid userId, Guid sessionFamilyId, long pts, CancellationToken ct = default) =>
        db.ExecuteAsync(@"
            INSERT INTO user_sync_state (user_id, session_family_id, acked_pts, updated_at)
            VALUES (@userId, @sessionFamilyId, @pts, now())
            ON CONFLICT (user_id, session_family_id) DO UPDATE
            SET acked_pts = GREATEST(user_sync_state.acked_pts, EXCLUDED.acked_pts), updated_at = now()",
            new { userId, sessionFamilyId, pts }, ct);

    public Task<int> DeleteOlderThanAsync(DateTime olderThan, int batchSize, CancellationToken ct = default) =>
        db.ExecuteAsync(@"
            DELETE FROM user_updates
            WHERE ctid IN (SELECT ctid FROM user_updates WHERE created_at < @olderThan LIMIT @batchSize)",
            new { olderThan, batchSize }, ct);
}
