using BasicApi.Extensions;
using BasicApi.Storage.Dto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BasicApi.Features.Messages;

[Authorize]
[ApiController]
[Route("api/search")]
[Produces("application/json")]
[Tags("Search")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
public class SearchController(IMessageService messages) : ControllerBase
{
    /// <summary>
    /// Search messages across all the caller's chats.
    /// </summary>
    /// <remarks>
    /// The same full-text search as in a chat (Russian morphology, words as prefixes), over every
    /// chat the caller is in now. Deleted messages, the caller's hidden ones and system messages are
    /// not found. Newest first; <c>nextCursor</c> — for the next page. Each hit is the message
    /// (<c>MessageDto</c>) and its chat: type, title or companion, avatar.
    ///
    /// Filters: <c>chatId</c>, <c>senderId</c>, <c>from</c>/<c>to</c> (the time, <c>to</c> exclusive),
    /// <c>type</c> — <c>text</c> or <c>media</c> (the caption is searched).
    ///
    /// Errors: <c>400 INVALID_QUERY</c> (shorter than 2 characters), <c>400 INVALID_FILTER</c>,
    /// <c>400 INVALID_CURSOR</c>, <c>403 NOT_A_MEMBER</c> (a chat filter for a chat the caller is not in).
    /// </remarks>
    [HttpGet("messages")]
    [ProducesResponseType(typeof(GlobalSearchResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SearchMessages(
        [FromQuery] string? q,
        [FromQuery] Guid? chatId,
        [FromQuery] Guid? senderId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? type,
        [FromQuery] string? cursor,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
        => Ok(await messages.SearchAllAsync(User.GetUserId(), q, new MessageSearchFilter
        {
            ChatId = chatId,
            SenderId = senderId,
            From = from,
            To = to,
            Type = type
        }, cursor, Math.Clamp(limit, 1, 100), ct));
}
