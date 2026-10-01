using BasicApi.Storage.Dto;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Storage.Repositories;

public sealed class UpdateJournalRepository(IDbSession db) : IUpdateJournal
{
    public Task AppendAsync(IReadOnlyCollection<Guid> userIds, string type, string payloadJson, CancellationToken ct = default)
    {
        if (userIds.Count == 0)
            return Task.CompletedTask;

        // Counters are taken in user_id order, so two single writes never wait on each other in a
        // cycle. A transaction that writes more than once (one person, then everyone) still can:
        // Postgres ends one of them and DbSession runs it again.
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

    public Task<IReadOnlyList<PointerMove>> DeliverUpToAsync(Guid userId, long pts, CancellationToken ct = default) =>
        // New messages the journal handed out between the furthest earlier ack of any device and
        // this one: per chat, the highest seq. Payloads are MessageDto in camelCase. The member rows
        // are locked before reading the old pointer, so a concurrent ack of another device either
        // sees this move or is seen by it; a chat the user has left since is skipped by the join.
        db.QueryAsync<PointerMove>(@"
            WITH prev AS (
                SELECT COALESCE(MAX(acked_pts), 0) AS pts FROM user_sync_state WHERE user_id = @userId
            ), reached AS (
                SELECT (u.payload->>'chatId')::uuid AS chat_id, MAX((u.payload->>'seq')::bigint) AS seq
                FROM user_updates u, prev
                WHERE u.user_id = @userId AND u.pts > prev.pts AND u.pts <= @pts AND u.type = 'MessageCreated'
                GROUP BY 1
            ), locked AS (
                SELECT cm.chat_id, cm.last_delivered_seq AS from_seq, r.seq AS to_seq
                FROM chat_members cm
                JOIN reached r ON r.chat_id = cm.chat_id
                WHERE cm.user_id = @userId AND cm.last_delivered_seq < r.seq
                FOR UPDATE OF cm
            ), moved AS (
                UPDATE chat_members cm SET last_delivered_seq = l.to_seq
                FROM locked l
                WHERE cm.chat_id = l.chat_id AND cm.user_id = @userId AND cm.last_delivered_seq < l.to_seq
                RETURNING cm.chat_id, l.from_seq, l.to_seq
            )
            SELECT chat_id AS ChatId, from_seq AS FromSeq, to_seq AS ToSeq FROM moved",
            new { userId, pts }, ct);

    public Task<int> DeleteOlderThanAsync(DateTime olderThan, int batchSize, CancellationToken ct = default) =>
        db.ExecuteAsync(@"
            DELETE FROM user_updates
            WHERE ctid IN (SELECT ctid FROM user_updates WHERE created_at < @olderThan LIMIT @batchSize)",
            new { olderThan, batchSize }, ct);
}
