using BasicApi.Extensions;
using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;
using BasicApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BasicApi.Features.Chats;

[Authorize]
[ApiController]
[Route("api/chats")]
[Produces("application/json")]
[Tags("Chats")]
public class ChatsController(
    IChatService chats,
    IChatStateService chatStates) : ControllerBase
{
    /// <summary>
    /// Get all chats for the current user
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<ChatListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetUserChats(CancellationToken ct)
        => Ok(await chats.GetUserChatsAsync(User.GetUserId(), ct));

    /// <summary>
    /// The user's chats, a page at a time.
    /// </summary>
    /// <remarks>
    /// The same <c>ChatListItemDto</c> as <c>GET /api/chats</c>, newest activity first (a new
    /// message, or creation). <c>nextCursor</c> — pass it as <c>cursor</c> for the next page; null —
    /// the end. A chat that gets a message while paging moves to the top: keep the list up to date
    /// from events, not by reloading pages.
    ///
    /// Errors: <c>400 INVALID_CURSOR</c>.
    /// </remarks>
    /// <param name="cursor">From the previous page; omit for the first.</param>
    /// <param name="limit">Chats per page (default 50, max 200).</param>
    /// <param name="archived">true — the archive instead of the main list.</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpGet("page")]
    [ProducesResponseType(typeof(CursorPaginatedResponse<ChatListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetUserChatsPage(
        [FromQuery] string? cursor, [FromQuery] int limit = 50, [FromQuery] bool archived = false,
        CancellationToken ct = default)
        => Ok(await chats.GetUserChatsPageAsync(User.GetUserId(), cursor, Math.Clamp(limit, 1, 200), ct, archived));

    /// <summary>
    /// Pin a chat on top of the list, or unpin it.
    /// </summary>
    /// <remarks>
    /// A newly pinned chat goes on top; at most 10 are pinned. A pinned chat leaves the archive.
    /// Answers with the pinned chats, top first; the caller's other devices get
    /// <c>PinnedChatsChanged</c> with the same list.
    ///
    /// Errors: <c>400 TOO_MANY_PINNED</c>, <c>403 NOT_A_MEMBER</c>.
    /// </remarks>
    [HttpPut("{chatId}/pinned")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(typeof(PinnedChatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SetPinned(Guid chatId, [FromBody] SetPinnedDto dto, CancellationToken ct)
        => Ok(await chatStates.SetPinnedAsync(User.GetUserId(), chatId, dto.Pinned, ct));

    /// <summary>
    /// Reorder the pinned chats.
    /// </summary>
    /// <remarks>
    /// <c>chatIds</c> — exactly the pinned chats, top first. Errors: <c>400 INVALID_REQUEST</c>.
    /// </remarks>
    [HttpPut("pinned")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(typeof(PinnedChatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReorderPinned([FromBody] PinnedChatsDto dto, CancellationToken ct)
        => Ok(await chatStates.ReorderPinnedAsync(User.GetUserId(), dto.ChatIds, ct));

    /// <summary>
    /// Move a chat to the archive, or back.
    /// </summary>
    /// <remarks>
    /// An archived chat is not in the main list (<c>GET /api/chats/page</c>), only in
    /// <c>GET /api/chats/page?archived=true</c>; it is unpinned. A new message brings it back, unless
    /// the chat is muted. The caller's other devices get <c>ChatStateChanged</c>.
    ///
    /// Errors: <c>403 NOT_A_MEMBER</c>.
    /// </remarks>
    [HttpPut("{chatId}/archived")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(typeof(ChatStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SetArchived(Guid chatId, [FromBody] SetArchivedDto dto, CancellationToken ct)
        => Ok(await chatStates.SetArchivedAsync(User.GetUserId(), chatId, dto.Archived, ct));

    /// <summary>
    /// Mute a chat, or unmute it.
    /// </summary>
    /// <remarks>
    /// <c>{ "muted": true, "until": "…" }</c> — until the moment; without <c>until</c> — for good;
    /// <c>{ "muted": false }</c> — unmute. A muted chat sends no notifications and does not leave the
    /// archive on a new message; the unread counter works as usual. The caller's other devices get
    /// <c>ChatStateChanged</c>.
    ///
    /// Errors: <c>400 INVALID_REQUEST</c> (<c>until</c> in the past), <c>403 NOT_A_MEMBER</c>.
    /// </remarks>
    [HttpPut("{chatId}/muted")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(typeof(ChatStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SetMuted(Guid chatId, [FromBody] SetMutedDto dto, CancellationToken ct)
        => Ok(await chatStates.SetMutedAsync(User.GetUserId(), chatId, dto.Muted, dto.Until, ct));

    /// <summary>
    /// Create a private chat with another user.
    /// Returns 200 if chat already exists, 201 if a new chat was created.
    /// </summary>
    /// <remarks>
    /// The body is a full <c>ChatListItemDto</c> — the same shape as an entry of
    /// <c>GET /api/chats</c> — so the client can insert the chat into its list
    /// without a follow-up request. The companion fields describe the *other*
    /// participant as seen by the caller.
    ///
    /// The other participant receives the same chat over SignalR as a
    /// <c>ChatCreated</c> event, built from their own point of view.
    /// </remarks>
    [HttpPost("private/{userId}")]
    [ProducesResponseType(typeof(ChatListItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ChatListItemDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreatePrivateChat(Guid userId, CancellationToken ct)
    {
        var result = await chats.GetOrCreatePrivateChatAsync(User.GetUserId(), userId, ct);
        return result.Created ? Created(string.Empty, result.Chat) : Ok(result.Chat);
    }

    /// <summary>
    /// Open "Saved Messages" — the caller's own chat with themselves.
    /// </summary>
    /// <remarks>
    /// One per user, created on first call: <c>201</c> then, <c>200</c> afterwards, the same
    /// <c>ChatListItemDto</c> as in <c>GET /api/chats</c> with <c>type: "saved"</c> and no
    /// title or companion (the client names it). Everything works as in any chat: send,
    /// forward into it, edit, delete, react. The caller's other devices learn about a new one
    /// through <c>/api/sync</c> (<c>ChatCreated</c>).
    /// </remarks>
    [HttpPost("saved")]
    [ProducesResponseType(typeof(ChatListItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ChatListItemDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> OpenSavedMessages(CancellationToken ct)
    {
        var result = await chats.GetOrCreateSavedChatAsync(User.GetUserId(), ct);
        return result.Created ? Created(string.Empty, result.Chat) : Ok(result.Chat);
    }

    /// <summary>
    /// Get chat details
    /// </summary>
    [HttpGet("{chatId}")]
    [ProducesResponseType(typeof(ChatDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChat(Guid chatId, CancellationToken ct)
        => Ok(await chats.GetChatDetailsAsync(chatId, User.GetUserId(), ct));

    /// <summary>
    /// Get a single chat in chat-list shape.
    /// </summary>
    /// <remarks>
    /// Returns the same <c>ChatListItemDto</c> as one entry of <c>GET /api/chats</c>,
    /// resolved for the caller: companion id/name/username for private chats,
    /// unread count and last message preview.
    ///
    /// Purpose: the client knows only a <c>chatId</c> (after a push, a deep link,
    /// or a <c>ChatListUpdated</c> event) and needs to render or refresh exactly
    /// one row without refetching the whole list.
    ///
    /// Use <c>GET /api/chats/{chatId}</c> instead when you need the participant list.
    /// </remarks>
    /// <param name="chatId">Chat ID</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpGet("{chatId}/item")]
    [ProducesResponseType(typeof(ChatListItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChatItem(Guid chatId, CancellationToken ct)
        => Ok(await chats.GetChatListItemAsync(chatId, User.GetUserId(), ct));

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
    /// <param name="ct">Request cancellation.</param>
    [HttpGet("search")]
    [ProducesResponseType(typeof(SearchChatsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SearchChats(
        [FromQuery] string q,
        [FromQuery] string? type,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
        => Ok(await chats.SearchChatsAsync(User.GetUserId(), q, type, Math.Clamp(limit, 1, 100), ct));
}
