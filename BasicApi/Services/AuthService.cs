using System.Text;
using BasicApi.Hubs;
using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Auth;
using BasicApi.Services;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Exceptions;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Services;

/// <summary>Sign-in, registration, refresh and revocation of sessions.</summary>
public sealed class AuthService(
    IUserRepository userRepository,
    IJwtService jwtService,
    ISessionService sessionService,
    HubConnectionRegistry hubConnections)
{
    /// <summary>bcrypt limit: anything beyond it is ignored.</summary>
    public const int MaxPasswordBytes = 72;

    /// <summary>
    /// Hash of a random password with the same cost as real ones: the password of a nonexistent
    /// user is checked against it, so that response time does not reveal whether the login exists.
    /// </summary>
    private static readonly string DummyPasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString());

    public async Task<AuthResponseDto> LoginAsync(
        LoginRequestDto request, string? userAgent = null, string? ip = null, CancellationToken ct = default)
    {
        var user = await userRepository.GetByUsernameOrEmailAsync(request.UsernameOrEmail, ct);

        // We always check the hash, even if the user does not exist: otherwise "no such user" would answer
        // instantly, and "wrong password" only after ~100 ms of bcrypt, and response time
        // could be used to enumerate existing logins.
        var passwordMatches = BCrypt.Net.BCrypt.Verify(request.Password, user?.PasswordHash ?? DummyPasswordHash);

        if (user == null || !passwordMatches)
            throw new UnauthorizedException("Invalid username/email or password", "INVALID_CREDENTIALS");

        // A deactivated account must not sign in even with the correct password.
        if (!user.IsActive)
            throw new UnauthorizedException("Account is deactivated", "USER_INACTIVE");

        await userRepository.UpdateLastLoginAsync(user.Id, DateTime.UtcNow, ct);

        return await sessionService.IssueForUserAsync(user, userAgent, ip, ct);
    }

    public async Task<AuthResponseDto> RegisterAsync(
        RegisterRequestDto request, string? userAgent = null, string? ip = null, CancellationToken ct = default)
    {
        // Edge whitespace is a typing accident, it does not go into the login or email.
        // Case is kept as the user typed it (for display), while uniqueness
        // and sign-in are case-insensitive (normalized columns in the database).
        request.Username = request.Username.Trim();
        request.Email = request.Email.Trim();

        // bcrypt considers only the first 72 bytes: with longer ones, two different passwords
        // sharing a prefix would match. We count UTF-8 bytes, not characters.
        if (Encoding.UTF8.GetByteCount(request.Password) > MaxPasswordBytes)
            throw new BadRequestException(
                $"Password must be at most {MaxPasswordBytes} bytes in UTF-8", "PASSWORD_TOO_LONG");

        var existingUser = await userRepository.GetByUsernameOrEmailAsync(request.Username, ct);

        if (existingUser != null)
            throw new ConflictException("Username already exists", "USERNAME_TAKEN");

        // Email uniqueness check
        existingUser = await userRepository.GetByUsernameOrEmailAsync(request.Email, ct);

        if (existingUser != null)
            throw new ConflictException("Email already exists", "EMAIL_TAKEN");

        var user = new User
        {
            Username = request.Username,
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            DisplayName = string.IsNullOrEmpty(request.DisplayName)
                ? request.Username
                : request.DisplayName,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            user.Id = await userRepository.CreateAsync(user, ct);
        }
        catch (DuplicateKeyException)
        {
            // The checks above are not atomic — a parallel registration could get in
            // first. The unique index caught it; we answer 409, not 500.
            throw new ConflictException("Username or email already exists", "USER_ALREADY_EXISTS");
        }

        return await sessionService.IssueForUserAsync(user, userAgent, ip, ct);
    }

    /// <summary>
    /// Exchanges a refresh token for a fresh access/refresh pair.
    /// </summary>
    public Task<AuthResponseDto> RefreshAsync(
        string refreshToken, string? userAgent = null, string? ip = null, CancellationToken ct = default) =>
        sessionService.RefreshAsync(refreshToken, userAgent, ip, ct);

    /// <summary>
    /// Ends the session behind the supplied refresh token.
    /// Idempotent: an unknown or already-revoked token still returns 200, so the
    /// endpoint cannot be used to probe which tokens exist.
    /// </summary>
    public async Task LogoutAsync(string? refreshToken, CancellationToken ct = default)
    {
        // We also close this sign-in's hub connections: otherwise the "signed-out" device
        // would keep receiving messages as long as it holds the connection.
        var familyId = await sessionService.RevokeAsync(refreshToken, ct);
        if (familyId is not null)
            hubConnections.AbortSessionFamily(familyId.Value);
    }

    /// <summary>
    /// Ends every session of the current user — "log out on all devices".
    /// The current access token keeps working for REST until it expires (minutes), but
    /// no new one can be obtained; open hub connections are closed right away and cannot
    /// be reopened with the old token.
    /// </summary>
    public async Task LogoutAllAsync(Guid userId, CancellationToken ct = default)
    {
        await sessionService.RevokeAllForUserAsync(userId, ct);
        hubConnections.AbortUser(userId);
    }

    /// <summary>
    /// Validates whether the given JWT access token is still valid.
    /// Returns userId, username and isValid flag.
    /// </summary>
    public ValidateTokenResponseDto ValidateToken(string token)
    {
        var isValid = jwtService.TryValidateToken(token, out var userId, out var username);

        return new ValidateTokenResponseDto
        {
            UserId = userId,
            Username = username,
            IsValid = isValid
        };
    }
}
