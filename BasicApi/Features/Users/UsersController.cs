using BasicApi.Models.Dto.Users;
using BasicApi.Extensions;
using BasicApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BasicApi.Features.Users;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[Tags("Users")]
public class UsersController(
    IUserService users, IPresenceService presence, IProfileService profile, IPrivacyService privacy) : ControllerBase
{
    /// <summary>
    /// Get a user's ID by username.
    /// </summary>
    /// <remarks>
    /// Case-insensitive. Only active users. Lookup by email is not supported on purpose:
    /// an email address must not reveal whether its owner has an account.
    /// </remarks>
    [Authorize]
    [HttpGet("GetUserId/{username}")]
    [ProducesResponseType(typeof(UserIdResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserId(string username, CancellationToken ct)
        => Ok(await users.GetUserIdAsync(username, ct));

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
    /// <param name="ct">Request cancellation.</param>
    [Authorize]
    [HttpGet("search")]
    [ProducesResponseType(typeof(SearchUsersResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchUsers(
        [FromQuery] string q,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
        => Ok(await users.SearchUsersAsync(User.GetUserId(), q, Math.Clamp(limit, 1, 100), ct));

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
    /// <param name="ct">Request cancellation.</param>
    [Authorize]
    [HttpGet("{userId:guid}")]
    [ProducesResponseType(typeof(UserProfileResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserProfile(Guid userId, CancellationToken ct)
        => Ok(await users.GetUserProfileAsync(userId, ct, User.GetUserId()));

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
    public async Task<IActionResult> GetOwnProfile(CancellationToken ct)
        => Ok(await users.GetOwnProfileAsync(User.GetUserId(), ct));

    /// <summary>
    /// Change the caller's profile.
    /// </summary>
    /// <remarks>
    /// <c>displayName</c> — the name shown to others, 1–100 characters after trimming. Answers with
    /// the own profile; the caller's other devices and everyone who shares a chat with them get
    /// <c>UserUpdated</c>. Messages already sent show the new name too: it is not copied into them.
    ///
    /// Errors: <c>400 INVALID_DISPLAY_NAME</c>.
    /// </remarks>
    [Authorize]
    [HttpPatch("me")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(typeof(OwnProfileResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto dto, CancellationToken ct)
        => Ok(await profile.UpdateAsync(User.GetUserId(), dto, ct));

    /// <summary>
    /// The caller's privacy settings.
    /// </summary>
    /// <remarks>
    /// <c>lastSeen</c>, <c>messages</c>, <c>groupAdd</c> — each <c>everybody</c>, <c>contacts</c> (those who
    /// share a chat with the caller) or <c>nobody</c>; never changed — everybody.
    /// </remarks>
    [Authorize]
    [HttpGet("me/privacy")]
    [ProducesResponseType(typeof(PrivacySettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPrivacy(CancellationToken ct)
        => Ok(await privacy.GetAsync(User.GetUserId(), ct));

    /// <summary>
    /// Change the caller's privacy settings.
    /// </summary>
    /// <remarks>
    /// Only the fields given change. Answers with all settings; the caller's other devices get
    /// <c>PrivacyUpdated</c>.
    ///
    /// - <c>lastSeen</c> — who sees online and last seen. It works both ways: whoever hides theirs
    ///   (<c>nobody</c>) does not see the others' either. Takes effect at once.
    /// - <c>messages</c> — who may start a private chat; an existing chat keeps working.
    /// - <c>groupAdd</c> — who may add the caller to groups.
    ///
    /// Errors: <c>400 INVALID_PRIVACY</c>.
    /// </remarks>
    [Authorize]
    [HttpPut("me/privacy")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(typeof(PrivacySettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdatePrivacy([FromBody] PrivacySettingsDto dto, CancellationToken ct)
        => Ok(await privacy.UpdateAsync(User.GetUserId(), dto, ct));

    /// <summary>
    /// Block a user.
    /// </summary>
    /// <remarks>
    /// In a private chat neither may write to the other (the caller gets <c>403 USER_BLOCKED</c>, the
    /// blocked one <c>403 PRIVACY_RESTRICTED</c> — the same as for privacy settings, so a block is not
    /// told). The blocked one cannot start a private chat with the caller or add them to groups, and
    /// sees neither their online, last seen nor avatar. Takes effect at once; the caller's devices
    /// get <c>BlockListChanged</c>. Blocking twice is not an error.
    ///
    /// Errors: <c>400 INVALID_REQUEST</c> (oneself), <c>404 USER_NOT_FOUND</c>.
    /// </remarks>
    [Authorize]
    [HttpPut("{userId:guid}/block")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Block(Guid userId, CancellationToken ct)
    {
        await privacy.SetBlockedAsync(User.GetUserId(), userId, blocked: true, ct);
        return NoContent();
    }

    /// <summary>
    /// Unblock a user.
    /// </summary>
    [Authorize]
    [HttpDelete("{userId:guid}/block")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unblock(Guid userId, CancellationToken ct)
    {
        await privacy.SetBlockedAsync(User.GetUserId(), userId, blocked: false, ct);
        return NoContent();
    }

    /// <summary>
    /// Whom the caller blocked, the latest first.
    /// </summary>
    [Authorize]
    [HttpGet("me/blocked")]
    [ProducesResponseType(typeof(IEnumerable<UserProfileResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBlocked(CancellationToken ct)
        => Ok(await privacy.GetBlockedAsync(User.GetUserId(), ct));

    /// <summary>
    /// Set the caller's avatar.
    /// </summary>
    /// <remarks>
    /// A photo the caller uploaded (<c>POST /api/media/uploads</c>, kind <c>photo</c>). Answers with
    /// the own profile; the caller's other devices and everyone who shares a chat with them get
    /// <c>UserUpdated</c>. Anyone signed in may download a user's avatar.
    ///
    /// Errors: <c>400 INVALID_AVATAR</c> (not a photo), <c>404 ATTACHMENT_NOT_FOUND</c>.
    /// </remarks>
    [Authorize]
    [HttpPut("me/avatar")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(typeof(OwnProfileResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetAvatar([FromBody] SetAvatarDto dto, CancellationToken ct)
        => Ok(await profile.SetAvatarAsync(User.GetUserId(), dto.AttachmentId, ct));

    /// <summary>
    /// Remove the caller's avatar.
    /// </summary>
    /// <remarks>The same as setting one, with <c>avatarId: null</c>.</remarks>
    [Authorize]
    [HttpDelete("me/avatar")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(typeof(OwnProfileResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveAvatar(CancellationToken ct)
        => Ok(await profile.SetAvatarAsync(User.GetUserId(), null, ct));

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
    public async Task<IActionResult> GetOnlineStatus(CancellationToken ct)
        => Ok(await presence.GetContactsOnlineAsync(User.GetUserId(), ct));

    /// <summary>
    /// Get the online status of a single user.
    /// </summary>
    /// <remarks>
    /// Unlike `GET /api/users/status`, this always answers about the user you asked for:
    /// `isOnline: false` is returned explicitly when they are offline.
    ///
    /// Purpose: opening a chat screen. The client knows the companion's id and needs
    /// their current presence right away, without waiting for a `UserOnlineChanged` event
    /// that may have fired while the app was closed.
    ///
    /// Visible only for users you share a chat with (and for yourself); anything else
    /// returns 404, so the endpoint cannot be used to probe whether an account exists.
    ///
    /// Sample request:
    ///   GET /api/users/3fa85f64-5717-4562-b3fc-2c963f66afa6/status
    /// </remarks>
    /// <param name="userId">User ID.</param>
    /// <param name="ct">Request cancellation.</param>
    [Authorize]
    [HttpGet("{userId:guid}/status")]
    [ProducesResponseType(typeof(UserStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserStatus(Guid userId, CancellationToken ct)
        => Ok(await presence.GetUserStatusAsync(User.GetUserId(), userId, ct));

    /// <summary>
    /// Get the online status of an explicit set of users.
    /// </summary>
    /// <remarks>
    /// Returns one entry per requested user, including offline ones (`isOnline: false`).
    /// IDs you share no chat with are silently omitted from the response.
    /// At most 200 IDs per request; duplicates are collapsed.
    ///
    /// Purpose: entering the chat list screen. Send the `companionId`s of the private
    /// chats currently on screen and get their presence in one round-trip, instead of
    /// relying on `UserOnlineChanged` events that fired while the app was closed.
    ///
    /// Sample request:
    ///   POST /api/users/status
    ///   { "userIds": ["3fa85f64-5717-4562-b3fc-2c963f66afa6"] }
    /// </remarks>
    [Authorize]
    [HttpPost("status")]
    [ProducesResponseType(typeof(UserStatusResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetUsersStatus([FromBody] UserStatusBatchRequestDto dto, CancellationToken ct)
        => Ok(await presence.GetUsersStatusAsync(User.GetUserId(), dto.UserIds, ct));

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
    public async Task<IActionResult> GetTypingStatus(CancellationToken ct)
        => Ok(await presence.GetTypingAsync(User.GetUserId(), ct));
}
