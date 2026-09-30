using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Storage.Repositories;

public class SessionRepository(IDbSession db) : ISessionRepository
{
    private const string InsertSql = @"
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
            var affected = await db.ExecuteAsync(
                markRotatedSql, new { sessionId, replacementId = replacement.Id, rotatedAt }, ct);

            if (affected == 0)
                return false;

            await db.ExecuteAsync(InsertSql, replacement, ct);
            return true;
        }, ct: ct);
    }

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

    public async Task RevokeAsync(Guid sessionId, DateTime revokedAt, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE sessions
            SET revoked_at = @revokedAt
            WHERE id = @sessionId AND revoked_at IS NULL";

        await db.ExecuteAsync(sql, new { sessionId, revokedAt }, ct);
    }

    public async Task<int> RevokeFamilyAsync(Guid familyId, DateTime revokedAt, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE sessions
            SET revoked_at = @revokedAt
            WHERE family_id = @familyId AND revoked_at IS NULL";

        return await db.ExecuteAsync(sql, new { familyId, revokedAt }, ct);
    }

    public Task<int> RevokeAllForUserExceptAsync(
        Guid userId, Guid? keepFamilyId, DateTime revokedAt, CancellationToken ct = default) =>
        db.ExecuteAsync(@"
            UPDATE sessions
            SET revoked_at = @revokedAt
            WHERE user_id = @userId AND revoked_at IS NULL
              AND family_id IS DISTINCT FROM @keepFamilyId",
            new { userId, keepFamilyId, revokedAt }, ct);

    public async Task<int> RevokeAllForUserAsync(Guid userId, DateTime revokedAt, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE sessions
            SET revoked_at = @revokedAt
            WHERE user_id = @userId AND revoked_at IS NULL";

        return await db.ExecuteAsync(sql, new { userId, revokedAt }, ct);
    }
}
