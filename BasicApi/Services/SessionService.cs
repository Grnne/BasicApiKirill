using System.Security.Cryptography;
using System.Text;
using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Auth;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Services;

/// <summary>
/// Custom (non-Identity) session handling built on rotating refresh tokens.
///
/// Rules that matter:
/// - only the SHA-256 hash of a refresh token is persisted;
/// - every refresh rotates the token — the old one stops working;
/// - a token replayed within the grace window is treated as a client race
///   (two parallel requests both hitting 401), not as theft;
/// - a token replayed after the grace window revokes the entire rotation chain.
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
            FamilyId = Guid.NewGuid(), // новый логин — новая цепочка ротаций
            RefreshTokenHash = HashRefreshToken(refreshToken),
            CreatedAt = now,
            ExpiresAt = now.AddDays(_refreshDays),
            UserAgent = Truncate(userAgent, 400),
            Ip = Truncate(ip, 64)
        };

        await sessionRepository.CreateAsync(session, ct);

        return BuildResponse(user, refreshToken, session.ExpiresAt);
    }

    public async Task<AuthResponseDto> RefreshAsync(string refreshToken, string? userAgent, string? ip, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var session = await sessionRepository.GetByRefreshTokenHashAsync(HashRefreshToken(refreshToken), ct)
            ?? throw new UnauthorizedException("Refresh token is not recognised", "INVALID_REFRESH_TOKEN");

        if (session.ExpiresAt <= now)
            throw new UnauthorizedException("Refresh token has expired", "REFRESH_TOKEN_EXPIRED");

        // Уже погашенная сессия: либо честный logout, либо гонка, либо кража.
        var withinGraceWindow = false;
        if (session.RevokedAt is not null)
        {
            if (session.ReplacedBySessionId is null)
                throw new UnauthorizedException("Session has been revoked", "SESSION_REVOKED");

            withinGraceWindow = (now - session.RevokedAt.Value).TotalSeconds <= _graceSeconds;

            // Ротация — ещё не гарантия, что цепочка жива: logout или logout-all могли
            // погасить преемника уже после неё. Без этой проверки до-ротационный токен
            // позволял бы обойти выход из аккаунта в течение всего grace-окна.
            if (withinGraceWindow && !await sessionRepository.HasLiveSessionInFamilyAsync(session.FamilyId, ct))
                throw new UnauthorizedException("Session has been revoked", "SESSION_REVOKED");

            if (!withinGraceWindow)
            {
                // Токен предъявлен повторно спустя длительное время — считаем скомпрометированным
                // и гасим всю цепочку, включая ту сессию, которой сейчас пользуется вор.
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
            FamilyId = session.FamilyId, // ротация остаётся в той же цепочке
            RefreshTokenHash = HashRefreshToken(newRefreshToken),
            CreatedAt = now,
            ExpiresAt = session.ExpiresAt, // окно не продлевается бесконечной ротацией
            UserAgent = Truncate(userAgent, 400),
            Ip = Truncate(ip, 64)
        };

        if (withinGraceWindow)
        {
            // Исходная строка уже ротирована — просто добавляем ещё одну сессию в семью.
            await sessionRepository.CreateAsync(replacement, ct);
        }
        else if (!await sessionRepository.TryRotateAsync(session.Id, replacement, now, ct))
        {
            // Кто-то ротировал эту сессию между SELECT и UPDATE. Это та же гонка,
            // просто пойманная на шаг позже — клиента выкидывать не за что.
            await sessionRepository.CreateAsync(replacement, ct);
        }

        return BuildResponse(user, newRefreshToken, replacement.ExpiresAt);
    }

    public async Task RevokeAsync(string? refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return;

        var session = await sessionRepository.GetByRefreshTokenHashAsync(HashRefreshToken(refreshToken), ct);
        if (session is null || session.RevokedAt is not null)
            return;

        await sessionRepository.RevokeAsync(session.Id, DateTime.UtcNow, ct);
    }

    public Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
        => sessionRepository.RevokeAllForUserAsync(userId, DateTime.UtcNow, ct);

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

    private AuthResponseDto BuildResponse(User user, string refreshToken, DateTime refreshExpiresAt) => new()
    {
        UserId = user.Id,
        Username = user.Username,
        Email = user.Email,
        DisplayName = user.DisplayName,
        Token = jwtService.GenerateToken(user.Id, user.Username, user.Email),
        ExpiresAt = jwtService.GetExpiryDate(),
        RefreshToken = refreshToken,
        RefreshTokenExpiresAt = refreshExpiresAt
    };

    private static string? Truncate(string? value, int maxLength)
        => value is null || value.Length <= maxLength ? value : value[..maxLength];
}
