using BasicApi.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BasicApi.Features.Auth;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Tags("Authentication")]
public class AuthController(AuthService auth, InviteService invites) : ControllerBase
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
        => Ok(await auth.LoginAsync(request, UserAgent, RemoteIp, HttpContext.RequestAborted));

    /// <summary>
    /// Register a new user
    /// </summary>
    /// <remarks>
    /// Registration signs the user in: the response carries the same access and
    /// refresh tokens as `POST /api/auth/login`.
    ///
    /// Whether one may register is the server's setting (`GET /api/auth/registration`):
    /// `403 REGISTRATION_CLOSED` when it is closed; by invitation, `inviteCode` must be a member's
    /// unused, unexpired invitation — `403 INVITE_INVALID` otherwise. A registration refused for
    /// another reason (a name taken) does not use the invitation up.
    /// </remarks>
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto request)
        => Created(string.Empty, await invites.AdmitAsync(request.InviteCode,
            ct => auth.RegisterAsync(request, UserAgent, RemoteIp, ct), HttpContext.RequestAborted));

    /// <summary>
    /// Who may register here
    /// </summary>
    /// <remarks>
    /// `open` — anyone; `closed` — nobody, accounts are made otherwise; `invite` — with a code a
    /// member made (`POST /api/auth/invites`). For the sign-up form, before signing in.
    /// </remarks>
    [AllowAnonymous]
    [HttpGet("registration")]
    [ProducesResponseType(typeof(RegistrationDto), StatusCodes.Status200OK)]
    public IActionResult Registration() => Ok(invites.Registration);

    /// <summary>
    /// Invite someone to register
    /// </summary>
    /// <remarks>
    /// A one-time code that lets one person register while registration is by invitation; it
    /// works for a week. The server keeps only its hash: the code is in this response only.
    ///
    /// Errors: `403 INVITES_DISABLED` — registration here is not by invitation;
    /// `409 TOO_MANY_INVITES` — too many unused invitations already.
    /// </remarks>
    [Authorize]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [HttpPost("invites")]
    [ProducesResponseType(typeof(InviteDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateInvite() =>
        Created(string.Empty, await invites.CreateAsync(User.GetUserId(), HttpContext.RequestAborted));

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
        => Ok(await auth.RefreshAsync(request.RefreshToken, UserAgent, RemoteIp, HttpContext.RequestAborted));

    /// <summary>
    /// Log out of the current session.
    /// </summary>
    /// <remarks>
    /// Ends the sign-in of the access token the request carries, and the session of the
    /// `refreshToken` in the body, if one is sent. A refresh token that was rotated meanwhile
    /// does not matter: the sign-in still ends.
    ///
    /// Idempotent: an unknown or already-revoked token also returns 200, so the endpoint
    /// cannot be used to find out which tokens exist. The access tokens of the ended sign-in
    /// stop working at once (401).
    /// </remarks>
    [Authorize]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequestDto? request)
    {
        await auth.LogoutAsync(request?.RefreshToken, User.GetSessionFamilyId(), HttpContext.RequestAborted);
        return Ok();
    }

    /// <summary>
    /// Change the password.
    /// </summary>
    /// <remarks>
    /// Needs the current password. Every other sign-in of the user ends at once — their refresh
    /// tokens stop working and their hub connections close; this device stays signed in. Access
    /// tokens already issued to other devices work until they expire (minutes). Limited like
    /// sign-in: 5 attempts a minute from one IP.
    ///
    /// Errors: <c>400 WRONG_PASSWORD</c>, <c>400 PASSWORD_TOO_LONG</c>, <c>400 VALIDATION_ERROR</c>
    /// (new password shorter than 6), <c>429 RATE_LIMITED</c>.
    /// </remarks>
    [Authorize]
    [HttpPost("password")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto request)
    {
        await auth.ChangePasswordAsync(User.GetUserId(), User.GetSessionFamilyId(),
            request.CurrentPassword, request.NewPassword, HttpContext.RequestAborted);
        return NoContent();
    }

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
    {
        await auth.LogoutAllAsync(User.GetUserId(), HttpContext.RequestAborted);
        return Ok();
    }

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
    public IActionResult ValidateToken()
    {
        var authHeader = Request.Headers.Authorization.ToString();
        var token = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authHeader["Bearer ".Length..]
            : string.Empty;

        return Ok(auth.ValidateToken(token));
    }
}
