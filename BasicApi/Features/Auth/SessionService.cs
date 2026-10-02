using System.Security.Cryptography;
using System.Text;
using BasicApi.Middleware.Exceptions;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Features.Auth;

/// <summary>
/// Sessions on rotating refresh tokens; only their SHA-256 hash is stored. A replay within the
/// grace window is a client race, after it theft that revokes the whole chain.
/// </summary>
public class SessionService(
    ISessionRepository sessionRepository,
    IUserRepository userRepository,
    IJwtService jwtService,
    IConfiguration configuration) : ISessionService
{
    private readonly int _refreshDays = int.TryParse(configuration["Jwt:RefreshTokenDays"], out var d) ? d : 30;

    /// <summary>
    /// How long an already-rotated refresh token keeps working. Covers the common
    /// mobile case where the app fires two requests at once, both get a 401 and
    /// both try to refresh — without it the loser would be logged out.
    /// </summary>
    private readonly int _graceSeconds = int.TryParse(configuration["Jwt:RefreshGraceSeconds"], out var g) ? g : 30;

    public async Task<AuthResponseDto> IssueForUserAsync(User user, string? userAgent, string? ip, CancellationToken ct = default)
    {
        var refreshToken = GenerateRefreshToken();
        var now = DateTime.UtcNow;

        var session = new Session
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FamilyId = Guid.NewGuid(), // a new login — a new rotation chain
            RefreshTokenHash = HashRefreshToken(refreshToken),
            CreatedAt = now,
            ExpiresAt = now.AddDays(_refreshDays),
            UserAgent = Truncate(userAgent, 400),
            Ip = Truncate(ip, 64)
        };

        await sessionRepository.CreateAsync(session, ct);

        return BuildResponse(user, refreshToken, session.ExpiresAt, session.FamilyId);
    }

    public async Task<AuthResponseDto> RefreshAsync(string refreshToken, string? userAgent, string? ip, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var session = await sessionRepository.GetByRefreshTokenHashAsync(HashRefreshToken(refreshToken), ct)
            ?? throw new UnauthorizedException("Refresh token is not recognised", "INVALID_REFRESH_TOKEN");

        if (session.ExpiresAt <= now)
            throw new UnauthorizedException("Refresh token has expired", "REFRESH_TOKEN_EXPIRED");

        // An already-consumed session: either an honest logout, a race, or theft.
        if (session.RevokedAt is not null)
        {
            if (session.ReplacedBySessionId is null)
                throw new UnauthorizedException("Session has been revoked", "SESSION_REVOKED");

            if ((now - session.RevokedAt.Value).TotalSeconds > _graceSeconds)
            {
                // The token was presented again after a long time — treated as compromised
                // and we revoke the whole chain, including the session the thief is currently using.
                await sessionRepository.RevokeFamilyAsync(session.FamilyId, now, ct);
                throw new UnauthorizedException("Refresh token has already been used", "REFRESH_TOKEN_REUSED");
            }
        }

        var user = await userRepository.GetByIdAsync(session.UserId, ct)
            ?? throw new UnauthorizedException("User no longer exists", "USER_NOT_FOUND");

        if (!user.IsActive)
        {
            await sessionRepository.RevokeAllForUserAsync(user.Id, now, ct);
            throw new UnauthorizedException("Account is deactivated", "USER_INACTIVE");
        }

        var newRefreshToken = GenerateRefreshToken();
        var replacement = new Session
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            FamilyId = session.FamilyId, // rotation stays in the same chain
            RefreshTokenHash = HashRefreshToken(newRefreshToken),
            CreatedAt = now,
            ExpiresAt = session.ExpiresAt, // the window is not extended by endless rotation
            UserAgent = Truncate(userAgent, 400),
            Ip = Truncate(ip, 64)
        };

        // Not rotated by this request: rotated already by a racing one of the same client (then it
        // gets a sibling session while the chain is live), or revoked by a sign-out meanwhile —
        // then nothing, or the sign-out would be undone by a refresh that read the row before it.
        var rotated = session.RevokedAt is null &&
            await sessionRepository.TryRotateAsync(session.Id, replacement, now, ct);
        if (!rotated && !await sessionRepository.TryAddToRotatedFamilyAsync(session.Id, replacement, ct))
            throw new UnauthorizedException("Session has been revoked", "SESSION_REVOKED");

        return BuildResponse(user, newRefreshToken, replacement.ExpiresAt, replacement.FamilyId);
    }

    public async Task<Guid?> RevokeAsync(string? refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return null;

        var session = await sessionRepository.GetByRefreshTokenHashAsync(HashRefreshToken(refreshToken), ct);
        if (session is null || session.RevokedAt is not null)
            return null;

        await sessionRepository.RevokeAsync(session.Id, DateTime.UtcNow, ct);
        return session.FamilyId;
    }

    public Task RevokeFamilyAsync(Guid sessionFamilyId, CancellationToken ct = default)
        => sessionRepository.RevokeFamilyAsync(sessionFamilyId, DateTime.UtcNow, ct);

    public Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
        => sessionRepository.RevokeAllForUserAsync(userId, DateTime.UtcNow, ct);

    public Task RevokeOthersAsync(Guid userId, Guid? keepFamilyId, CancellationToken ct = default)
        => sessionRepository.RevokeAllForUserExceptAsync(userId, keepFamilyId, DateTime.UtcNow, ct);

    public Task<bool> IsSessionFamilyLiveAsync(Guid sessionFamilyId, CancellationToken ct = default)
        => sessionRepository.HasLiveSessionInFamilyAsync(sessionFamilyId, ct);

    public Task<IReadOnlyCollection<Guid>> GetLiveSessionFamiliesAsync(
        IReadOnlyCollection<Guid> sessionFamilyIds, CancellationToken ct = default)
        => sessionRepository.GetLiveFamiliesAsync(sessionFamilyIds, ct);

    /// <summary>
    /// SHA-256 hex of the refresh token. Refresh tokens are 256 bits of CSPRNG output,
    /// so a plain hash is enough — unlike passwords there is nothing to brute-force.
    /// </summary>
    public static string HashRefreshToken(string refreshToken)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken))).ToLowerInvariant();

    private static string GenerateRefreshToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    private AuthResponseDto BuildResponse(User user, string refreshToken, DateTime refreshExpiresAt, Guid sessionFamilyId) => new()
    {
        UserId = user.Id,
        Username = user.Username,
        Email = user.Email,
        DisplayName = user.DisplayName,
        Token = jwtService.GenerateToken(user.Id, user.Username, user.Email, sessionFamilyId),
        ExpiresAt = jwtService.GetExpiryDate(),
        RefreshToken = refreshToken,
        RefreshTokenExpiresAt = refreshExpiresAt
    };

    private static string? Truncate(string? value, int maxLength)
        => value is null || value.Length <= maxLength ? value : value[..maxLength];
}
