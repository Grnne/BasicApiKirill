using BasicApi.Models.Dto.Users;
using BasicApi.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BasicApi.Features.Users;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[Tags("Users")]
public class UsersController(UsersHandler handlers) : ControllerBase
{
    /// <summary>
    /// Get a user's ID by username or email.
    /// </summary>
    [Authorize]
    [HttpGet("GetUserId/{usernameOrEmail}")]
    [ProducesResponseType(typeof(UserIdResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserId(string usernameOrEmail)
        => await handlers.GetUserIdAsync(usernameOrEmail);

    /// <summary>
    /// Search users by display name or username (Telegram-style ILIKE search).
    /// </summary>
    /// <remarks>
    /// Searches active users by display name or username using case-insensitive partial matching.
    /// Excludes the current user from results.
    ///
    /// Sample request:
    ///   GET /api/users/search?q=alice&amp;limit=20
    /// </remarks>
    /// <param name="q">Search query (minimum 1 character).</param>
    /// <param name="limit">Max results (default 20, max 100).</param>
    [Authorize]
    [HttpGet("search")]
    [ProducesResponseType(typeof(SearchUsersResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchUsers(
        [FromQuery] string q,
        [FromQuery] int limit = 20)
        => await handlers.SearchUsersAsync(User.GetUserId(), q, Math.Clamp(limit, 1, 100));

    /// <summary>
    /// Get a user's public profile by ID.
    /// </summary>
    /// <remarks>
    /// Returns the user's ID, username and display name.
    /// Email is not exposed — it is private to the account owner; use `GET /api/users/me`
    /// for your own profile. Online status is not exposed here either — use
    /// `GET /api/users/status`, which is scoped to members of your own chats.
    ///
    /// Sample request:
    ///   GET /api/users/3fa85f64-5717-4562-b3fc-2c963f66afa6
    /// </remarks>
    /// <param name="userId">User ID.</param>
    [Authorize]
    [HttpGet("{userId:guid}")]
    [ProducesResponseType(typeof(UserProfileResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserProfile(Guid userId)
        => await handlers.GetUserProfileAsync(userId);

    /// <summary>
    /// Get the current user's own profile, resolved from the JWT.
    /// </summary>
    /// <remarks>
    /// Intended for session restore: a client that kept the token but lost its local
    /// user data (cache cleared, app reinstalled) can recover the same user fields
    /// login/register returns, without asking for credentials again.
    ///
    /// Returns userId, username, email and displayName — i.e. the login response
    /// minus the token. Unlike `GET /api/users/{userId}` this includes the email,
    /// since it is the caller's own data.
    ///
    /// A 404 here means the token is valid but the account no longer exists —
    /// the client should discard the token and show the login screen.
    ///
    /// Sample request:
    ///   GET /api/users/me
    /// </remarks>
    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(OwnProfileResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOwnProfile()
        => await handlers.GetOwnProfileAsync(User.GetUserId());

    /// <summary>
    /// Get online status of all chat members for the current user.
    /// Returns only users who are currently online.
    /// </summary>
    /// <remarks>
    /// Sample request:
    ///   GET /api/users/status
    /// Returns a list of online users with their userIds.
    /// </remarks>
    [Authorize]
    [HttpGet("status")]
    [ProducesResponseType(typeof(UserStatusResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOnlineStatus()
        => await handlers.GetOnlineStatusAsync(User.GetUserId());

    /// <summary>
    /// Get typing status across all user's chats.
    /// </summary>
    /// <remarks>
    /// Returns a list of users currently typing, grouped by chat.
    /// Sample request:
    ///   GET /api/users/typing
    /// </remarks>
    [Authorize]
    [HttpGet("typing")]
    [ProducesResponseType(typeof(TypingStatusResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTypingStatus()
        => await handlers.GetTypingStatusAsync(User.GetUserId());
}
