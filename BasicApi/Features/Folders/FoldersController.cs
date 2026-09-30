using BasicApi.Extensions;
using BasicApi.Models.Dto.Chat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BasicApi.Features.Folders;

[Authorize]
[ApiController]
[Route("api/folders")]
[Produces("application/json")]
[Tags("Folders")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
public class FoldersController(IFolderService folders) : ControllerBase
{
    /// <summary>
    /// The caller's folders, in their order.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<FolderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFolders(CancellationToken ct)
        => Ok(await folders.GetAllAsync(User.GetUserId(), ct));

    /// <summary>
    /// Create a folder.
    /// </summary>
    /// <remarks>
    /// A folder shows the chats listed in <c>chatIds</c> and, by filters, all private chats
    /// (<c>includePrivate</c>) or all groups (<c>includeGroups</c>) outside the archive; with
    /// <c>onlyUnread</c> — only those with something unread. <c>pinnedChatIds</c> — pinned inside
    /// the folder, top first (they are in the folder too). Up to 20 folders, 200 listed and 10 pinned
    /// chats in each; the new folder goes last. The caller's devices get <c>FoldersChanged</c> with
    /// all folders.
    ///
    /// Errors: <c>400 INVALID_TITLE</c> (1–64 characters), <c>400 TOO_MANY_FOLDERS</c>,
    /// <c>400 INVALID_REQUEST</c> (limits; a chat the caller is not in).
    /// </remarks>
    [HttpPost]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(typeof(FolderDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateFolder([FromBody] SaveFolderDto dto, CancellationToken ct)
        => Created(string.Empty, await folders.CreateAsync(User.GetUserId(), dto, ct));

    /// <summary>
    /// Change a folder: only the fields given (lists are replaced whole).
    /// </summary>
    /// <remarks>Errors: as for creation, and <c>404 FOLDER_NOT_FOUND</c>.</remarks>
    [HttpPatch("{folderId:guid}")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(typeof(FolderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateFolder(Guid folderId, [FromBody] SaveFolderDto dto, CancellationToken ct)
        => Ok(await folders.UpdateAsync(User.GetUserId(), folderId, dto, ct));

    /// <summary>
    /// Delete a folder; its chats stay where they are.
    /// </summary>
    [HttpDelete("{folderId:guid}")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteFolder(Guid folderId, CancellationToken ct)
    {
        await folders.DeleteAsync(User.GetUserId(), folderId, ct);
        return NoContent();
    }

    /// <summary>
    /// Reorder the folders: exactly the caller's folders, in the new order.
    /// </summary>
    [HttpPut("order")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(typeof(IEnumerable<FolderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReorderFolders([FromBody] FolderOrderDto dto, CancellationToken ct)
        => Ok(await folders.ReorderAsync(User.GetUserId(), dto.FolderIds, ct));

    /// <summary>
    /// The folder's chats, a page at a time.
    /// </summary>
    /// <remarks>
    /// The same <c>ChatListItemDto</c> as <c>GET /api/chats/page</c>: the first page starts with the
    /// chats pinned in the folder (outside the limit), then the others by activity.
    ///
    /// Errors: <c>400 INVALID_CURSOR</c>, <c>404 FOLDER_NOT_FOUND</c>.
    /// </remarks>
    [HttpGet("{folderId:guid}/chats")]
    [ProducesResponseType(typeof(Models.Dto.Message.CursorPaginatedResponse<ChatListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFolderChats(
        Guid folderId, [FromQuery] string? cursor, [FromQuery] int limit = 50, CancellationToken ct = default)
        => Ok(await folders.GetChatsAsync(User.GetUserId(), folderId, cursor, Math.Clamp(limit, 1, 200), ct));
}
