using BasicApi.Models.Dto.Auth;
using BasicApi.Storage.Entities;

namespace BasicApi.Services;

/// <summary>
/// Refresh-token sessions: issuing on login/register, rotation on refresh,
/// and revocation on logout or detected token theft.
/// </summary>
public interface ISessionService
{
    /// <summary>
    /// Starts a new session for a user who has just proven their identity
    /// (login or registration) and returns the access/refresh pair.
    /// </summary>
    Task<AuthResponseDto> IssueForUserAsync(User user, string? userAgent, string? ip, CancellationToken ct = default);

    /// <summary>
    /// Exchanges a refresh token for a new pair, rotating the session.
    /// Throws <see cref="Middleware.Exceptions.UnauthorizedException"/> when the token is
    /// unknown, expired, revoked, replayed after the grace window, or the user is gone/inactive.
    /// </summary>
    Task<AuthResponseDto> RefreshAsync(string refreshToken, string? userAgent, string? ip, CancellationToken ct = default);

    /// <summary>
    /// Ends the session behind the given refresh token. Idempotent and silent about
    /// whether the token existed — logout must not double as a token oracle.
    /// </summary>
    /// <returns>Session family of the revoked session, or null when nothing was revoked.</returns>
    Task<Guid?> RevokeAsync(string? refreshToken, CancellationToken ct = default);

    /// <summary>Ends every live session of a user ("log out everywhere").</summary>
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Whether the sign-in behind an access token (its <c>sid</c>) is still open.
    /// An access token outlives logout by minutes; the hub checks this on connect.
    /// </summary>
    Task<bool> IsSessionFamilyLiveAsync(Guid sessionFamilyId, CancellationToken ct = default);
}
