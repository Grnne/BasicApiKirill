using System.Security.Cryptography;
using System.Text;
using BasicApi.Hubs;
using BasicApi.Middleware.Exceptions;
using BasicApi.Services;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Exceptions;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Features.Auth;

/// <summary>Sign-in, registration, refresh and revocation of sessions.</summary>
public sealed class AuthService(
    IUserRepository userRepository,
    IJwtService jwtService,
    ISessionService sessionService,
    IDeviceRepository devices,
    HubConnectionRegistry hubConnections,
    SessionLiveness liveness)
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

        var passwordMatches = BCrypt.Net.BCrypt.Verify(request.Password, user?.PasswordHash ?? DummyPasswordHash);

        if (user == null || !passwordMatches)
            throw new UnauthorizedException("Invalid username/email or password", "INVALID_CREDENTIALS");

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

    public Task<AuthResponseDto> RefreshAsync(
        string refreshToken, string? userAgent = null, string? ip = null, CancellationToken ct = default) =>
        sessionService.RefreshAsync(refreshToken, userAgent, ip, ct);

    /// <summary>
    /// Ends the sign-in of the caller's access token and the session behind the supplied refresh
    /// token. Idempotent: an unknown or already-revoked token still returns 200, so the endpoint
    /// cannot be used to probe which tokens exist.
    /// </summary>
    public async Task LogoutAsync(string? refreshToken, Guid? callerSessionFamilyId = null, CancellationToken ct = default)
    {
        // The refresh token alone is not enough: a client whose access token expired refreshes the
        // pair on the 401 and retries with the token it had read before, which is rotated by then.
        var families = new HashSet<Guid>();
        if (await sessionService.RevokeAsync(refreshToken, ct) is { } byToken)
            families.Add(byToken);
        if (callerSessionFamilyId is { } caller && families.Add(caller))
            await sessionService.RevokeFamilyAsync(caller, ct);

        // We also close the hub connections: otherwise the "signed-out" device would keep
        // receiving messages as long as it holds the connection.
        foreach (var familyId in families)
        {
            liveness.Forget(familyId);
            await devices.DeleteAsync(familyId, ct);
            hubConnections.AbortSessionFamily(familyId);
        }
    }

    /// <summary>
    /// Changes the password and ends every other sign-in of the user, with their hub connections;
    /// the one that asked stays. Errors: 400 <c>WRONG_PASSWORD</c>/<c>PASSWORD_TOO_LONG</c>.
    /// </summary>
    public async Task ChangePasswordAsync(
        Guid userId, Guid? sessionFamilyId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        if (Encoding.UTF8.GetByteCount(newPassword) > MaxPasswordBytes)
            throw new BadRequestException(
                $"Password must be at most {MaxPasswordBytes} bytes in UTF-8", "PASSWORD_TOO_LONG");

        var user = await userRepository.GetByIdAsync(userId, ct)
            ?? throw new UnauthorizedException("Authentication required", "TOKEN_MISSING_OR_EXPIRED");
        // Not 401: the client would take it for an expired token and refresh it.
        if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
            throw new BadRequestException("The current password is wrong", "WRONG_PASSWORD");

        await userRepository.SetPasswordHashAsync(userId, BCrypt.Net.BCrypt.HashPassword(newPassword), ct);
        // Whoever might know the old password is signed out everywhere else, at once.
        await sessionService.RevokeOthersAsync(userId, sessionFamilyId, ct);
        await devices.DeleteAllExceptAsync(userId, sessionFamilyId, ct);
        liveness.ForgetAll();
        hubConnections.AbortUserExcept(userId, sessionFamilyId);
    }

    /// <summary>
    /// The administrator's reset for a user who forgot the password: a new random password, every
    /// session and push subscription of the user ended. Null — no active user with this login. Run
    /// from the command line it is another process: the running server's own caches do not learn of
    /// it: requests stop within 15 s, hub connections at its next session check (a minute).
    /// </summary>
    public async Task<string?> ResetPasswordAsync(string username, CancellationToken ct = default)
    {
        if (await userRepository.GetIdByUsernameAsync(username, ct) is not { } userId)
            return null;

        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(15))
            .Replace('+', '-').Replace('/', '_');
        await userRepository.SetPasswordHashAsync(userId, BCrypt.Net.BCrypt.HashPassword(password), ct);
        await sessionService.RevokeAllForUserAsync(userId, ct);
        await devices.DeleteAllExceptAsync(userId, null, ct);
        liveness.ForgetAll();
        hubConnections.AbortUser(userId);
        return password;
    }

    /// <summary>
    /// Ends every session of the current user — "log out on all devices", this one included:
    /// their access tokens stop working and open hub connections are closed right away.
    /// </summary>
    public async Task LogoutAllAsync(Guid userId, CancellationToken ct = default)
    {
        await sessionService.RevokeAllForUserAsync(userId, ct);
        await devices.DeleteAllExceptAsync(userId, null, ct);
        liveness.ForgetAll();
        hubConnections.AbortUser(userId);
    }

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
