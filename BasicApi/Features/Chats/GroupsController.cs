using BasicApi.Extensions;
using BasicApi.Models.Dto.Chat;
using BasicApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BasicApi.Features.Chats;

/// <summary>Groups: creating them, members, roles and settings (plan 2, F3).</summary>
[Authorize]
[ApiController]
[Route("api/chats")]
[Produces("application/json")]
[Tags("Groups")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
public class GroupsController(IGroupService groups) : ControllerBase
{
    /// <summary>
    /// Create a group.
    /// </summary>
    /// <remarks>
    /// The caller becomes its owner; <c>memberIds</c> (may be empty) become members. The group
    /// opens with a system message "group created". Up to <c>Groups:MaxMembers</c> (500) members,
    /// the owner included.
    ///
    /// The body is the caller's <c>ChatListItemDto</c> (<c>type: "group"</c>). The members receive
    /// <c>ChatCreated</c> with their own card; the caller's other devices learn of the group through
    /// <c>/api/sync</c>.
    ///
    /// Errors: <c>400 INVALID_TITLE</c> (1–128 characters after trimming), <c>400 TOO_MANY_MEMBERS</c>,
    /// <c>404 USER_NOT_FOUND</c> (a member does not exist or is deactivated), <c>429 RATE_LIMITED</c>.
    /// </remarks>
    /// <param name="dto">Title and members</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpPost("groups")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(typeof(ChatListItemDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> CreateGroup([FromBody] CreateGroupDto dto, CancellationToken ct) =>
        Created(string.Empty, await groups.CreateAsync(User.GetUserId(), dto.Title, dto.MemberIds, ct));

    /// <summary>
    /// Rename a group or change what its members may do by default.
    /// </summary>
    /// <remarks>
    /// Only the given fields change. <c>title</c> — needs <c>changeInfo</c>; members get a system
    /// message "title changed". <c>memberPermissions</c> — the owner or an admin with
    /// <c>removeMembers</c>; only <c>sendMessages</c>, <c>sendMedia</c>, <c>addMembers</c>,
    /// <c>changeInfo</c>, and a given field changes, an omitted one stays (<c>{ "sendMessages": false }</c>
    /// makes the group read-only for members). Members' own overrides still win.
    ///
    /// The body is the group's title and default permissions now; members receive <c>ChatUpdated</c>
    /// with the same. No change — no event.
    ///
    /// Errors: <c>400 NOT_A_GROUP</c>, <c>400 INVALID_TITLE</c>, <c>400 INVALID_PERMISSIONS</c>,
    /// <c>403 NOT_A_MEMBER</c>, <c>403 PERMISSION_DENIED</c>, <c>429 RATE_LIMITED</c>.
    /// </remarks>
    /// <param name="chatId">Group ID</param>
    /// <param name="dto">What to change</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpPatch("{chatId}")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(typeof(ChatUpdatedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> UpdateGroup(Guid chatId, [FromBody] UpdateGroupDto dto, CancellationToken ct) =>
        Ok(await groups.UpdateAsync(chatId, User.GetUserId(), dto.Title, dto.MemberPermissions, ct));

    /// <summary>
    /// Set the group's photo.
    /// </summary>
    /// <remarks>
    /// A photo the caller uploaded (<c>POST /api/media/uploads</c>, kind <c>photo</c>). Needs the
    /// <c>changeInfo</c> permission. Members get a system message (<c>photo_changed</c>) and
    /// <c>ChatUpdated</c> with <c>avatarId</c>; the same photo again changes nothing.
    ///
    /// Errors: <c>400 NOT_A_GROUP</c>, <c>400 INVALID_AVATAR</c> (not a photo), <c>403 NOT_A_MEMBER</c>,
    /// <c>403 PERMISSION_DENIED</c>, <c>404 ATTACHMENT_NOT_FOUND</c>.
    /// </remarks>
    [HttpPut("{chatId}/avatar")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(typeof(ChatUpdatedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetAvatar(Guid chatId, [FromBody] Models.Dto.Users.SetAvatarDto dto, CancellationToken ct) =>
        Ok(await groups.SetAvatarAsync(chatId, User.GetUserId(), dto.AttachmentId, ct));

    /// <summary>
    /// Remove the group's photo.
    /// </summary>
    /// <remarks>
    /// Like setting one: <c>changeInfo</c>, a system message (<c>photo_removed</c>), <c>ChatUpdated</c>
    /// with <c>avatarId: null</c>.
    /// </remarks>
    [HttpDelete("{chatId}/avatar")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(typeof(ChatUpdatedDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RemoveAvatar(Guid chatId, CancellationToken ct) =>
        Ok(await groups.SetAvatarAsync(chatId, User.GetUserId(), null, ct));

    /// <summary>
    /// Delete a group for everyone.
    /// </summary>
    /// <remarks>
    /// The owner only. The group and its history are gone for all members; they receive
    /// <c>ChatDeleted</c>, and their connections stop getting the group's events.
    ///
    /// Errors: <c>400 NOT_A_GROUP</c>, <c>403 NOT_A_MEMBER</c>, <c>403 PERMISSION_DENIED</c>,
    /// <c>429 RATE_LIMITED</c>.
    /// </remarks>
    /// <param name="chatId">Group ID</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpDelete("{chatId}")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> DeleteGroup(Guid chatId, CancellationToken ct)
    {
        await groups.DeleteAsync(chatId, User.GetUserId(), ct);
        return NoContent();
    }

    /// <summary>
    /// The group's action log.
    /// </summary>
    /// <remarks>
    /// For the owner and admins. Newest first; <c>nextCursor</c> — pass it as <c>cursor</c> for
    /// older entries. Actions: <c>group_created</c>, <c>title_changed</c>,
    /// <c>member_permissions_changed</c>, <c>members_added</c>, <c>member_removed</c>,
    /// <c>member_left</c>, <c>role_changed</c>, <c>permissions_changed</c>,
    /// <c>ownership_transferred</c>, <c>message_deleted</c> (someone else's message deleted by an
    /// admin); <c>data</c> holds the details.
    ///
    /// Errors: <c>400 NOT_A_GROUP</c>, <c>400 INVALID_CURSOR</c>, <c>403 NOT_A_MEMBER</c>,
    /// <c>403 PERMISSION_DENIED</c> (not an admin).
    /// </remarks>
    /// <param name="chatId">Group ID</param>
    /// <param name="cursor">From the previous page; omit for the newest.</param>
    /// <param name="limit">Entries per page (default 50, max 200).</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpGet("{chatId}/audit")]
    [ProducesResponseType(typeof(AuditPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAudit(
        Guid chatId, [FromQuery] string? cursor, [FromQuery] int limit = 50, CancellationToken ct = default) =>
        Ok(await groups.GetAuditAsync(chatId, User.GetUserId(), cursor, Math.Clamp(limit, 1, 200), ct));

    /// <summary>
    /// Members of a group with their roles and permissions.
    /// </summary>
    /// <remarks>
    /// Owner first, then admins, then members by joining time. <c>permissions</c> is what the member
    /// may do now: the role, the group's defaults and their own overrides together.
    ///
    /// Errors: <c>400 NOT_A_GROUP</c>, <c>403 NOT_A_MEMBER</c>.
    /// </remarks>
    /// <param name="chatId">Group ID</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpGet("{chatId}/members")]
    [ProducesResponseType(typeof(IEnumerable<GroupMemberDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMembers(Guid chatId, CancellationToken ct) =>
        Ok(await groups.GetMembersAsync(chatId, User.GetUserId(), ct));

    /// <summary>
    /// Add members to a group.
    /// </summary>
    /// <remarks>
    /// Needs <c>addMembers</c>. Those already in the group are skipped; the body lists who was
    /// added (empty — nobody new). New members see the whole history, but only what comes after
    /// joining counts as unread; they receive <c>ChatCreated</c>. The members already there receive
    /// <c>MemberAdded</c> and a system message "members added".
    ///
    /// Errors: <c>400 NOT_A_GROUP</c>, <c>400 INVALID_REQUEST</c> (nobody besides the caller),
    /// <c>400 TOO_MANY_MEMBERS</c>, <c>403 NOT_A_MEMBER</c>, <c>403 PERMISSION_DENIED</c>,
    /// <c>404 USER_NOT_FOUND</c>, <c>429 RATE_LIMITED</c>.
    /// </remarks>
    /// <param name="chatId">Group ID</param>
    /// <param name="dto">Who to add</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpPost("{chatId}/members")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(typeof(IEnumerable<GroupMemberDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> AddMembers(Guid chatId, [FromBody] AddMembersDto dto, CancellationToken ct) =>
        Ok(await groups.AddMembersAsync(chatId, User.GetUserId(), dto.UserIds, ct));

    /// <summary>
    /// Remove a member, or leave the group.
    /// </summary>
    /// <remarks>
    /// Another member — the owner removes anyone, an admin with <c>removeMembers</c> — members only.
    /// Oneself — leaving: the owner hands the group over to the longest-standing admin (or member),
    /// the last member deletes it. The removed one loses the chat with its history.
    ///
    /// Everyone, the removed one included, receives <c>MemberRemoved</c>; the rest also get a system
    /// message. The removed one's connections stop getting the chat's events.
    ///
    /// Errors: <c>400 NOT_A_GROUP</c>, <c>403 NOT_A_MEMBER</c>, <c>403 PERMISSION_DENIED</c>,
    /// <c>404 MEMBER_NOT_FOUND</c>, <c>429 RATE_LIMITED</c>.
    /// </remarks>
    /// <param name="chatId">Group ID</param>
    /// <param name="userId">The member; the caller's own id — leave</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpDelete("{chatId}/members/{userId}")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RemoveMember(Guid chatId, Guid userId, CancellationToken ct)
    {
        await groups.RemoveMemberAsync(chatId, User.GetUserId(), userId, ct);
        return NoContent();
    }

    /// <summary>
    /// Change a member's role, or hand the group over.
    /// </summary>
    /// <remarks>
    /// - <c>admin</c> — make a member an admin: the owner, or an admin with <c>addAdmins</c>.
    /// - <c>member</c> — make an admin a member again: the owner.
    /// - <c>owner</c> — hand the group over: the owner; they become an admin.
    ///
    /// A new role starts without the member's own permission overrides. Members receive
    /// <c>MemberUpdated</c> (for a handover — about both). The same role again changes nothing.
    ///
    /// Errors: <c>400 NOT_A_GROUP</c>, <c>400 INVALID_ROLE</c>, <c>403 NOT_A_MEMBER</c>,
    /// <c>403 PERMISSION_DENIED</c>, <c>404 MEMBER_NOT_FOUND</c>, <c>429 RATE_LIMITED</c>.
    /// </remarks>
    /// <param name="chatId">Group ID</param>
    /// <param name="userId">The member</param>
    /// <param name="dto">The new role</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpPut("{chatId}/members/{userId}/role")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(typeof(GroupMemberDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SetRole(Guid chatId, Guid userId, [FromBody] SetRoleDto dto, CancellationToken ct) =>
        Ok(await groups.SetRoleAsync(chatId, User.GetUserId(), userId, dto.Role, ct));

    /// <summary>
    /// Set a member's own permissions.
    /// </summary>
    /// <remarks>
    /// Replaces the member's overrides: a given field overrides the role and the group's defaults,
    /// an omitted one follows them (<c>{}</c> removes all overrides). For a member — the owner or an
    /// admin with <c>removeMembers</c>, and only <c>sendMessages</c>, <c>sendMedia</c>,
    /// <c>addMembers</c>, <c>changeInfo</c>; for an admin — the owner, any permission. Members receive
    /// <c>MemberUpdated</c>.
    ///
    /// Errors: <c>400 NOT_A_GROUP</c>, <c>400 INVALID_PERMISSIONS</c> (an admin permission for a member),
    /// <c>403 NOT_A_MEMBER</c>, <c>403 PERMISSION_DENIED</c>, <c>404 MEMBER_NOT_FOUND</c>,
    /// <c>429 RATE_LIMITED</c>.
    /// </remarks>
    /// <param name="chatId">Group ID</param>
    /// <param name="userId">The member</param>
    /// <param name="dto">The overrides</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpPut("{chatId}/members/{userId}/permissions")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(typeof(GroupMemberDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SetPermissions(
        Guid chatId, Guid userId, [FromBody] PermissionsPatchDto dto, CancellationToken ct) =>
        Ok(await groups.SetPermissionsAsync(chatId, User.GetUserId(), userId, dto, ct));
}
