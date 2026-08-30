using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Dapper;

namespace BasicApi.Storage.Repositories;

public class SessionRepository(IDbConnectionFactory connectionFactory) : ISessionRepository
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
        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(InsertSql, session, cancellationToken: ct));
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

        using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Session>(
            new CommandDefinition(sql, new { refreshTokenHash }, cancellationToken: ct));
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

        using var connection = connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            var affected = await connection.ExecuteAsync(new CommandDefinition(
                markRotatedSql,
                new { sessionId, replacementId = replacement.Id, rotatedAt },
                transaction,
                cancellationToken: ct));

            if (affected == 0)
            {
                transaction.Rollback();
                return false;
            }

            await connection.ExecuteAsync(new CommandDefinition(
                InsertSql, replacement, transaction, cancellationToken: ct));

            transaction.Commit();
            return true;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<bool> HasLiveSessionInFamilyAsync(Guid familyId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT EXISTS(
                SELECT 1 FROM sessions
                WHERE family_id = @familyId AND revoked_at IS NULL
            )";

        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(sql, new { familyId }, cancellationToken: ct));
    }

    public async Task RevokeAsync(Guid sessionId, DateTime revokedAt, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE sessions
            SET revoked_at = @revokedAt
            WHERE id = @sessionId AND revoked_at IS NULL";

        using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql, new { sessionId, revokedAt }, cancellationToken: ct));
    }

    public async Task<int> RevokeFamilyAsync(Guid familyId, DateTime revokedAt, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE sessions
            SET revoked_at = @revokedAt
            WHERE family_id = @familyId AND revoked_at IS NULL";

        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition(
            sql, new { familyId, revokedAt }, cancellationToken: ct));
    }

    public async Task<int> RevokeAllForUserAsync(Guid userId, DateTime revokedAt, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE sessions
            SET revoked_at = @revokedAt
            WHERE user_id = @userId AND revoked_at IS NULL";

        using var connection = connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition(
            sql, new { userId, revokedAt }, cancellationToken: ct));
    }
}
