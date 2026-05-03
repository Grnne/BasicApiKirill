using BasicApi.Extensions;
using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BasicApi.Features.Chats;

[Authorize]
[ApiController]
[Route("api/chats")]
[Produces("application/json")]
[Tags("Chats")]
public class ChatsController(ChatsHandler handlers) : ControllerBase
{
    /// <summary>
    /// Get all chats for the current user
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ChatListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetUserChats()
        => await handlers.GetUserChatsAsync(User.GetUserId());

    /// <summary>
    /// Create a private chat with another user.
    /// Returns 200 if chat already exists, 201 if a new chat was created.
    /// </summary>
    [HttpPost("private/{userId}")]
        [ProducesResponseType(typeof(CreateChatResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CreateChatResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreatePrivateChat(Guid userId)
        => await handlers.CreatePrivateChatAsync(User.GetUserId(), userId);

    /// <summary>
    /// Get chat details
    /// </summary>
    [HttpGet("{chatId}")]
    [ProducesResponseType(typeof(ChatDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChat(Guid chatId)
        => await handlers.GetChatAsync(chatId, User.GetUserId());

    /// <summary>
    /// Get messages with cursor-based pagination.
    /// </summary>
    /// <remarks>
    /// Returns messages ordered chronologically (oldest first).
    /// Use the `cursor` parameter from the previous response's `nextCursor` field
    /// to fetch the next (older) page. When `cursor` is omitted, returns the most recent messages.
    /// 
    /// The response includes:
    /// - `items`: the messages in this page
    /// - `nextCursor`: pass this as `cursor` to get the next page (null = no more pages)
    /// - `hasMore`: whether more messages exist beyond this page
    /// </remarks>
    /// <param name="chatId">Chat ID</param>
    /// <param name="cursor">Cursor from previous response (optional). Omit for the first page.</param>
    /// <param name="limit">Number of messages per page (default 20, max 100).</param>
    [HttpGet("{chatId}/messages/cursor")]
    [ProducesResponseType(typeof(CursorPaginatedResponse<MessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMessagesCursor(
        Guid chatId,
        [FromQuery] string? cursor,
        [FromQuery] int limit = 20)
        => await handlers.GetMessagesCursorAsync(chatId, User.GetUserId(), cursor, Math.Clamp(limit, 1, 100));

    /// <summary>
    /// Get messages around a specific date.
    /// </summary>
    /// <remarks>
    /// Returns a page of messages cursor-based, with the most recent message at or before
    /// the given date as the last item in the page.
    /// Use the returned `nextCursor` to scroll further back.
    /// 
    /// Purpose: "Jump to March 15", "Open chat where I left off", "Go to unread messages".
    /// </remarks>
    /// <param name="chatId">Chat ID</param>
    /// <param name="date">Target date (ISO 8601). Finds messages at or before this date.</param>
    /// <param name="limit">Number of messages per page (default 20, max 100).</param>
    [HttpGet("{chatId}/messages/at")]
    [ProducesResponseType(typeof(CursorPaginatedResponse<MessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMessagesAt(
        Guid chatId,
        [FromQuery] DateTime date,
        [FromQuery] int limit = 20)
        => await handlers.GetMessagesAtAsync(chatId, User.GetUserId(), date, Math.Clamp(limit, 1, 100));

        /// <summary>
    /// Mark messages as read
    /// </summary>
    [HttpPost("{chatId}/read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> MarkRead(Guid chatId, [FromBody] MarkMessageReadDto dto)
        => await handlers.MarkReadAsync(chatId, User.GetUserId(), dto.LastMessageId);

    /// <summary>
    /// Full-text search for messages within a chat.
    /// </summary>
    /// <remarks>
    /// Searches messages by text content using PostgreSQL full-text search.
    /// Returns messages ordered chronologically (oldest first, newest matches).
    /// Supports cursor-based pagination — use the returned <c>nextCursor</c> to fetch older results.
    /// 
    /// The response includes:
    /// - <c>items</c>: matching messages with sender names
    /// - <c>nextCursor</c>: pass this as <c>cursor</c> to get the next page (null = no more pages)
    /// - <c>hasMore</c>: whether more matches exist beyond this page
    /// - <c>query</c>: the original search query
    /// - <c>totalCount</c>: total number of matches for this query
    /// </remarks>
    /// <param name="chatId">Chat ID to search within.</param>
    /// <param name="q">Search query (minimum 2 characters).</param>
    /// <param name="cursor">Cursor from previous response (optional).</param>
    /// <param name="limit">Number of results per page (default 20, max 100).</param>
    [HttpGet("{chatId}/messages/search")]
    [ProducesResponseType(typeof(SearchMessagesResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SearchMessages(
        Guid chatId,
        [FromQuery] string q,
        [FromQuery] string? cursor,
        [FromQuery] int limit = 20)
        => await handlers.SearchMessagesAsync(chatId, User.GetUserId(), q, cursor, Math.Clamp(limit, 1, 100));

    /// <summary>
    /// Search user's chats by query.
    /// </summary>
    /// <remarks>
    /// Searches the current user's chats.
    /// For group chats: searches by chat title (ILIKE).
    /// For private chats: searches by companion display name or username (ILIKE).
    /// 
    /// Use the optional <c>type</c> parameter to filter results:
    /// - <c>type=group</c> — search only group chats
    /// - <c>type=private</c> — search only private chats
    /// - omit <c>type</c> — search both
    /// 
    /// Sample requests:
    /// - GET /api/chats/search?q=team&amp;type=group&amp;limit=20
    /// - GET /api/chats/search?q=alice&amp;type=private&amp;limit=20
    /// - GET /api/chats/search?q=something&amp;limit=20
    /// </remarks>
    /// <param name="q">Search query.</param>
    /// <param name="type">Optional type filter: "group", "private", or empty for both.</param>
    /// <param name="limit">Max results (default 20, max 100).</param>
    [HttpGet("search")]
    [ProducesResponseType(typeof(SearchChatsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SearchChats(
        [FromQuery] string q,
        [FromQuery] string? type,
        [FromQuery] int limit = 20)
        => await handlers.SearchChatsAsync(User.GetUserId(), q, type, Math.Clamp(limit, 1, 100));
}