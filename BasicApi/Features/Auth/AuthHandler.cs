using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Auth;
using BasicApi.Services;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Exceptions;
using BasicApi.Storage.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BasicApi.Features.Auth;

public class AuthHandler(
    IUserRepository userRepository,
    IJwtService jwtService,
    ISessionService sessionService)
{
    public async Task<IActionResult> LoginAsync(
        LoginRequestDto request, string? userAgent = null, string? ip = null, CancellationToken ct = default)
    {
        var user = await userRepository.GetByUsernameOrEmailAsync(request.UsernameOrEmail, ct);

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedException("Invalid username/email or password", "INVALID_CREDENTIALS");

        // Деактивированный аккаунт не должен входить, даже зная правильный пароль.
        if (!user.IsActive)
            throw new UnauthorizedException("Account is deactivated", "USER_INACTIVE");

        await userRepository.UpdateLastLoginAsync(user.Id, DateTime.UtcNow, ct);

        var response = await sessionService.IssueForUserAsync(user, userAgent, ip, ct);
        return new OkObjectResult(response);
    }

    public async Task<IActionResult> RegisterAsync(
        RegisterRequestDto request, string? userAgent = null, string? ip = null, CancellationToken ct = default)
    {
        var existingUser = await userRepository.GetByUsernameOrEmailAsync(request.Username, ct);

        if (existingUser != null)
            throw new ConflictException("Username already exists", "USERNAME_TAKEN");

        // Проверка уникальности email
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
            // Проверки выше не атомарны — параллельная регистрация могла успеть
            // раньше. Уникальный индекс поймал, отвечаем 409, а не 500.
            throw new ConflictException("Username or email already exists", "USER_ALREADY_EXISTS");
        }

        var response = await sessionService.IssueForUserAsync(user, userAgent, ip, ct);
        return new CreatedResult(string.Empty, response);
    }

    /// <summary>
    /// Exchanges a refresh token for a fresh access/refresh pair.
    /// </summary>
    public async Task<IActionResult> RefreshAsync(
        string refreshToken, string? userAgent = null, string? ip = null, CancellationToken ct = default)
    {
        var response = await sessionService.RefreshAsync(refreshToken, userAgent, ip, ct);
        return new OkObjectResult(response);
    }

    /// <summary>
    /// Ends the session behind the supplied refresh token.
    /// Idempotent: an unknown or already-revoked token still returns 200, so the
    /// endpoint cannot be used to probe which tokens exist.
    /// </summary>
    public async Task<IActionResult> LogoutAsync(string? refreshToken, CancellationToken ct = default)
    {
        await sessionService.RevokeAsync(refreshToken, ct);
        return new OkResult();
    }

    /// <summary>
    /// Ends every session of the current user — "log out on all devices".
    /// The current access token keeps working until it expires (minutes), but no
    /// new one can be obtained.
    /// </summary>
    public async Task<IActionResult> LogoutAllAsync(Guid userId, CancellationToken ct = default)
    {
        await sessionService.RevokeAllForUserAsync(userId, ct);
        return new OkResult();
    }

    /// <summary>
    /// Validates whether the given JWT access token is still valid.
    /// Returns userId, username and isValid flag.
    /// </summary>
    public Task<IActionResult> ValidateTokenAsync(string token)
    {
        var isValid = jwtService.TryValidateToken(token, out var userId, out var username);

        return Task.FromResult<IActionResult>(new OkObjectResult(new ValidateTokenResponseDto
        {
            UserId = userId,
            Username = username,
            IsValid = isValid
        }));
    }
}
