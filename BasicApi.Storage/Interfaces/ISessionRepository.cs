using BasicApi.Storage.Entities;

namespace BasicApi.Storage.Interfaces;

public interface ISessionRepository
{
    /// <summary>Inserts the session; the first one of a chain also registers its device.</summary>
    Task CreateAsync(Session session, CancellationToken ct = default);

    /// <summary>
    /// Looks a session up by the hash of a presented refresh token.
    /// Returns revoked and expired rows too — the caller has to tell
    /// "already rotated" (possible theft) from "unknown token".
    /// </summary>
    Task<Session?> GetByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken ct = default);

    /// <summary>
    /// Atomically rotates a session: marks it replaced and inserts the successor
    /// in one transaction, but only if it is still active. Returns false when the
    /// row was already rotated or revoked by a concurrent request.
    /// </summary>
    Task<bool> TryRotateAsync(Guid sessionId, Session replacement, DateTime rotatedAt, CancellationToken ct = default);

    /// <summary>
    /// Whether the rotation chain still has a session that is neither revoked nor expired.
    /// Used to reject a grace-window replay after the chain was ended by logout:
    /// the presented token was only "rotated", but its successor is already dead.
    /// </summary>
    Task<bool> HasLiveSessionInFamilyAsync(Guid familyId, CancellationToken ct = default);

    /// <summary>Which of the given rotation chains are still live (same rule as above), in one query.</summary>
    Task<IReadOnlyCollection<Guid>> GetLiveFamiliesAsync(IReadOnlyCollection<Guid> familyIds, CancellationToken ct = default);

    Task RevokeAsync(Guid sessionId, DateTime revokedAt, CancellationToken ct = default);

    /// <summary>Revokes every live session of one rotation chain — used on detected token reuse.</summary>
    Task<int> RevokeFamilyAsync(Guid familyId, DateTime revokedAt, CancellationToken ct = default);

    /// <summary>Revokes every live session of a user — "log out everywhere".</summary>
    Task<int> RevokeAllForUserAsync(Guid userId, DateTime revokedAt, CancellationToken ct = default);

    /// <summary>Revokes every live session of the user except those of one sign-in (null — all).</summary>
    Task<int> RevokeAllForUserExceptAsync(Guid userId, Guid? keepFamilyId, DateTime revokedAt, CancellationToken ct = default);
}
