using BasicApi.Extensions;
using BasicApi.Models.Dto.Auth;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BasicApi.Features.Auth;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Tags("Authentication")]
public class AuthController(AuthHandler handler) : ControllerBase
{
    /// <summary>
    /// Authenticate a user
    /// </summary>
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
        => await handler.LoginAsync(request);

    /// <summary>
    /// Register a new user
    /// </summary>
    [AllowAnonymous]
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto request)
        => await handler.RegisterAsync(request);

    /// <summary>
    /// Logout the current user.
    /// </summary>
    /// <remarks>
    /// Currently a no-op since we use stateless JWTs.
    /// Future: add token to blacklist, notify SignalR connections.
    /// The client should discard the token on its side.
    /// </remarks>
    [Authorize]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout()
        => await handler.LogoutAsync(User.GetUserId());

    /// <summary>
    /// Validate whether a JWT token is still valid.
    /// </summary>
    /// <remarks>
    /// Returns userId, username, and isValid flag.
    /// Use this endpoint to check if the token has expired or is malformed.
    /// Unlike other endpoints, this one is anonymous so it can test tokens
    /// that may already be expired or tampered with.
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
