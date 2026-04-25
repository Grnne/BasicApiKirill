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
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetUserChats()
        => await handlers.GetUserChatsAsync(User.GetUserId());

    /// <summary>
    /// Create a private chat with another user
    /// </summary>
    [HttpPost("private/{userId}")]
        [ProducesResponseType(typeof(CreateChatResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CreateChatResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreatePrivateChat(Guid userId)
        => await handlers.CreatePrivateChatAsync(User.GetUserId(), userId);

    /// <summary>
    /// Get chat details
    /// </summary>
    [HttpGet("{chatId}")]
    [ProducesResponseType(typeof(ChatDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> MarkRead(Guid chatId, [FromBody] MarkMessageReadDto dto)
        => await handlers.MarkReadAsync(chatId, User.GetUserId(), dto.LastMessageId);
}