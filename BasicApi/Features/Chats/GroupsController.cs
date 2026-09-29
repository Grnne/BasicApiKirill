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
