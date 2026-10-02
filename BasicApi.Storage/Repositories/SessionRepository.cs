using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Storage.Repositories;

/// <remarks>
/// A user's sessions are added and revoked under one lock per user. Without it a revocation's
/// UPDATE misses a session a racing refresh inserts after the UPDATE took its snapshot, and the
/// signed-out device keeps a working token. Each revocation takes the lock in its own statement
/// first, so its UPDATE then sees everything committed before.
/// </remarks>
public class SessionRepository(IDbSession db) : ISessionRepository
{
    private const string LockUserSql = "SELECT pg_advisory_xact_lock(hashtextextended(@userId::text, 0))";
    private const string InsertSql = @"
        WITH device AS (
            INSERT INTO devices (id, user_id, created_at)
            VALUES (@FamilyId, @UserId, @CreatedAt)
            ON CONFLICT (id) DO NOTHING
        )
        INSERT INTO sessions
            (id, user_id, family_id, refresh_token_hash, created_at, expires_at,
             revoked_at, replaced_by_session_id, user_agent, ip)
        VALUES
            (@Id, @UserId, @FamilyId, @RefreshTokenHash, @CreatedAt, @ExpiresAt,
             @RevokedAt, @ReplacedBySessionId, @UserAgent, @Ip)";

    public async Task CreateAsync(Session session, CancellationToken ct = default)
    {
        await db.ExecuteAsync(InsertSql, session, ct);
    }

    public async Task<Session?> GetByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT
                id AS Id,
                user_id AS UserId,
                family_id AS FamilyId,
                refresh_token_hash AS RefreshTokenHash,
                created_at AS CreatedAt,
                expires_at AS ExpiresAt,
                revoked_at AS RevokedAt,
                replaced_by_session_id AS ReplacedBySessionId,
                user_agent AS UserAgent,
                ip AS Ip
            FROM sessions
            WHERE refresh_token_hash = @refreshTokenHash
            LIMIT 1";

        return await db.QueryFirstOrDefaultAsync<Session>(sql, new { refreshTokenHash }, ct);
    }

    public async Task<bool> TryRotateAsync(Guid sessionId, Session replacement, DateTime rotatedAt, CancellationToken ct = default)
    {
        // The UPDATE only matches a session that is still live, so two concurrent
        // refreshes cannot both rotate the same row — the loser gets 0 rows back
        // and is told to handle it as a race instead of inserting a second successor.
        const string markRotatedSql = @"
            UPDATE sessions
            SET revoked_at = @rotatedAt,
                replaced_by_session_id = @replacementId
            WHERE id = @sessionId AND revoked_at IS NULL";

        return await db.InTransactionAsync(async ct =>
        {
            await db.ExecuteAsync(LockUserSql, new { userId = replacement.UserId }, ct);
            var affected = await db.ExecuteAsync(
                markRotatedSql, new { sessionId, replacementId = replacement.Id, rotatedAt }, ct);

            if (affected == 0)
                return false;

            await db.ExecuteAsync(InsertSql, replacement, ct);
            return true;
        }, ct: ct);
    }

    public Task<bool> TryAddToRotatedFamilyAsync(Guid rotatedSessionId, Session replacement, CancellationToken ct = default) =>
        db.InTransactionAsync(async ct =>
        {
            await db.ExecuteAsync(LockUserSql, new { userId = replacement.UserId }, ct);
            return await db.ExecuteAsync(@"
                INSERT INTO sessions
                    (id, user_id, family_id, refresh_token_hash, created_at, expires_at, user_agent, ip)
                SELECT @Id, @UserId, @FamilyId, @RefreshTokenHash, @CreatedAt, @ExpiresAt, @UserAgent, @Ip
                WHERE EXISTS (
                        SELECT 1 FROM sessions
                        WHERE id = @rotatedSessionId AND replaced_by_session_id IS NOT NULL)
                  AND EXISTS (
                        SELECT 1 FROM sessions
                        WHERE family_id = @FamilyId AND revoked_at IS NULL AND expires_at > now())",
                new
                {
                    replacement.Id, replacement.UserId, replacement.FamilyId, replacement.RefreshTokenHash,
                    replacement.CreatedAt, replacement.ExpiresAt, replacement.UserAgent, replacement.Ip,
                    rotatedSessionId
                }, ct) == 1;
        }, ct: ct);

    public async Task<bool> HasLiveSessionInFamilyAsync(Guid familyId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT EXISTS(
                SELECT 1 FROM sessions
                WHERE family_id = @familyId AND revoked_at IS NULL AND expires_at > now()
            )";

        return await db.ExecuteScalarAsync<bool>(sql, new { familyId }, ct);
    }

    public async Task<IReadOnlyCollection<Guid>> GetLiveFamiliesAsync(
        IReadOnlyCollection<Guid> familyIds, CancellationToken ct = default)
    {
        if (familyIds.Count == 0)
            return [];

        const string sql = @"
            SELECT DISTINCT family_id FROM sessions
            WHERE family_id = ANY(@familyIds) AND revoked_at IS NULL AND expires_at > now()";

        return await db.QueryAsync<Guid>(sql, new { familyIds = familyIds.ToArray() }, ct);
    }

    public Task RevokeAsync(Guid sessionId, DateTime revokedAt, CancellationToken ct = default) =>
        db.InTransactionAsync(async ct =>
        {
            await db.ExecuteAsync(
                "SELECT pg_advisory_xact_lock(hashtextextended(user_id::text, 0)) FROM sessions WHERE id = @sessionId",
                new { sessionId }, ct);
            return await db.ExecuteAsync(@"
                UPDATE sessions
                SET revoked_at = @revokedAt
                WHERE id = @sessionId AND revoked_at IS NULL",
                new { sessionId, revokedAt }, ct);
        }, ct: ct);

    public Task<int> RevokeFamilyAsync(Guid familyId, DateTime revokedAt, CancellationToken ct = default) =>
        db.InTransactionAsync(async ct =>
        {
            // A chain belongs to one user: any of its rows names whose lock to take.
            await db.ExecuteAsync(@"
                SELECT pg_advisory_xact_lock(hashtextextended(user_id::text, 0))
                FROM (SELECT user_id FROM sessions WHERE family_id = @familyId LIMIT 1) s",
                new { familyId }, ct);
            return await db.ExecuteAsync(@"
                UPDATE sessions
                SET revoked_at = @revokedAt
                WHERE family_id = @familyId AND revoked_at IS NULL",
                new { familyId, revokedAt }, ct);
        }, ct: ct);

    public Task<int> RevokeAllForUserExceptAsync(
        Guid userId, Guid? keepFamilyId, DateTime revokedAt, CancellationToken ct = default) =>
        db.InTransactionAsync(async ct =>
        {
            await db.ExecuteAsync(LockUserSql, new { userId }, ct);
            return await db.ExecuteAsync(@"
                UPDATE sessions
                SET revoked_at = @revokedAt
                WHERE user_id = @userId AND revoked_at IS NULL
                  AND family_id IS DISTINCT FROM @keepFamilyId",
                new { userId, keepFamilyId, revokedAt }, ct);
        }, ct: ct);

    public Task<int> RevokeAllForUserAsync(Guid userId, DateTime revokedAt, CancellationToken ct = default) =>
        RevokeAllForUserExceptAsync(userId, null, revokedAt, ct);
}
