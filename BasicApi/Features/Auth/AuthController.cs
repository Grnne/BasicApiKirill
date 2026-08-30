using BasicApi.Extensions;
using BasicApi.Models.Dto.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BasicApi.Features.Auth;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Tags("Authentication")]
public class AuthController(AuthHandler handler) : ControllerBase
{
    private string? UserAgent => Request.Headers.UserAgent.FirstOrDefault();
    private string? RemoteIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>
    /// Authenticate a user
    /// </summary>
    /// <remarks>
    /// Returns a short-lived `token` (access) and a long-lived `refreshToken`.
    /// Store both; when the access token expires, call `POST /api/auth/refresh`
    /// instead of asking for the password again.
    /// </remarks>
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
        => await handler.LoginAsync(request, UserAgent, RemoteIp, HttpContext.RequestAborted);

    /// <summary>
    /// Register a new user
    /// </summary>
    /// <remarks>
    /// Registration signs the user in: the response carries the same access and
    /// refresh tokens as `POST /api/auth/login`.
    /// </remarks>
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto request)
        => await handler.RegisterAsync(request, UserAgent, RemoteIp, HttpContext.RequestAborted);

    /// <summary>
    /// Exchange a refresh token for a new access/refresh pair.
    /// </summary>
    /// <remarks>
    /// Call this when the access token has expired (or is about to). The endpoint is
    /// anonymous on purpose — the expired access token is not required, and not accepted
    /// as proof of anything.
    ///
    /// **Rotation:** every successful call invalidates the refresh token you sent and
    /// returns a new one. Always persist the new value.
    ///
    /// **Parallel refreshes are safe.** A token that was rotated less than 30 seconds ago
    /// still works, so two requests racing after a 401 both succeed instead of logging the
    /// user out. Replaying a token after that window is treated as theft and revokes every
    /// session in that login's chain (`REFRESH_TOKEN_REUSED`).
    ///
    /// Error codes: `INVALID_REFRESH_TOKEN`, `REFRESH_TOKEN_EXPIRED`, `SESSION_REVOKED`,
    /// `REFRESH_TOKEN_REUSED`, `USER_INACTIVE`, `USER_NOT_FOUND` — all as 401.
    /// Any of them means the same thing for the client: show the login screen.
    /// </remarks>
    [AllowAnonymous]
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequestDto request)
        => await handler.RefreshAsync(request.RefreshToken, UserAgent, RemoteIp, HttpContext.RequestAborted);

    /// <summary>
    /// Log out of the current session.
    /// </summary>
    /// <remarks>
    /// Send the session's `refreshToken` in the body — that is what actually gets revoked.
    /// Without it the server has nothing to invalidate and the session stays alive until
    /// it expires.
    ///
    /// Idempotent: an unknown or already-revoked token also returns 200, so the endpoint
    /// cannot be used to find out which tokens exist. The access token keeps working until
    /// it expires (minutes) — discard it client-side.
    /// </remarks>
    [Authorize]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequestDto? request)
        => await handler.LogoutAsync(request?.RefreshToken, HttpContext.RequestAborted);

    /// <summary>
    /// Log out of every session on all devices.
    /// </summary>
    /// <remarks>
    /// Revokes all refresh tokens of the current user — use it after a password change or
    /// when a device is lost. Already-issued access tokens stay valid until they expire.
    /// </remarks>
    [Authorize]
    [HttpPost("logout-all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LogoutAll()
        => await handler.LogoutAllAsync(User.GetUserId(), HttpContext.RequestAborted);

    /// <summary>
    /// Validate whether a JWT access token is still valid.
    /// </summary>
    /// <remarks>
    /// Returns userId, username, and isValid flag.
    /// Use this endpoint to check if the access token has expired or is malformed.
    /// Unlike other endpoints, this one is anonymous so it can test tokens
    /// that may already be expired or tampered with.
    ///
    /// This says nothing about the refresh token or the session — a valid answer here
    /// does not mean the session is still alive, and an invalid one does not mean the
    /// user must log in again (try `POST /api/auth/refresh` first).
    ///
    /// Sample request:
    ///   GET /api/auth/validate
    /// Headers: Authorization: Bearer {token}
    /// </remarks>
    [AllowAnonymous]
    [HttpGet("validate")]
    [ProducesResponseType(typeof(ValidateTokenResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ValidateToken()
    {
        // Extract raw token from Authorization header
        var authHeader = Request.Headers.Authorization.ToString();
        var token = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authHeader["Bearer ".Length..]
            : string.Empty;

        return await handler.ValidateTokenAsync(token);
    }
}
