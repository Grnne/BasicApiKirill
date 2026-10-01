using BasicApi.Extensions;
using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;
using BasicApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BasicApi.Features.Messages;

[Authorize]
[ApiController]
[Route("api/chats")]
[Produces("application/json")]
[Tags("Chats")]
public class MessagesController(
    IMessageService messages,
    IReactionService reactions,
    IReadStateService readState,
    IDraftService drafts,
    IPresenceService presence) : ControllerBase
{
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
    /// <param name="ct">Request cancellation.</param>
    [HttpGet("{chatId}/messages/cursor")]
    [ProducesResponseType(typeof(CursorPaginatedResponse<MessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMessagesCursor(
        Guid chatId,
        [FromQuery] string? cursor,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
        => Ok(await messages.GetPageAsync(chatId, User.GetUserId(), cursor, Math.Clamp(limit, 1, 100), ct));

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
    /// <param name="ct">Request cancellation.</param>
    [HttpGet("{chatId}/messages/at")]
    [ProducesResponseType(typeof(CursorPaginatedResponse<MessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMessagesAt(
        Guid chatId,
        [FromQuery] DateTime date,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
        => Ok(await messages.GetPageAtAsync(chatId, User.GetUserId(), date, Math.Clamp(limit, 1, 100), ct));

    /// <summary>
    /// Get the history around a message.
    /// </summary>
    /// <remarks>
    /// For opening a chat at a found message or the original of a reply: up to half of `limit`
    /// messages before it, the message itself, the rest after it — oldest first.
    /// Older pages: `GET …/messages/cursor?cursor={nextCursor}` while `hasMore`; newer ones:
    /// `GET …/messages/after?seq={last item's seq}` while `hasNewer`.
    ///
    /// Errors: <c>403 NOT_A_MEMBER</c>, <c>404 MESSAGE_NOT_FOUND</c> — not in this chat, deleted
    /// for everyone or by the caller for themselves.
    /// </remarks>
    /// <param name="chatId">Chat ID</param>
    /// <param name="messageId">The message to open the history at.</param>
    /// <param name="limit">Messages in the window (default 30, max 100).</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpGet("{chatId}/messages/{messageId:guid}/context")]
    [ProducesResponseType(typeof(MessageWindowDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMessageContext(
        Guid chatId,
        Guid messageId,
        [FromQuery] int limit = 30,
        CancellationToken ct = default)
        => Ok(await messages.GetContextAsync(chatId, User.GetUserId(), messageId, Math.Clamp(limit, 1, 100), ct));

    /// <summary>
    /// Get messages newer than a seq.
    /// </summary>
    /// <remarks>
    /// Paging forward from history opened in the middle (`…/context`, `…/messages/at`): the
    /// messages after `seq`, oldest first; `hasNewer` — there are more after the last item.
    ///
    /// Errors: <c>403 NOT_A_MEMBER</c>.
    /// </remarks>
    /// <param name="chatId">Chat ID</param>
    /// <param name="seq">The seq of the newest message the client already has.</param>
    /// <param name="limit">Number of messages per page (default 30, max 100).</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpGet("{chatId}/messages/after")]
    [ProducesResponseType(typeof(MessageWindowDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMessagesAfter(
        Guid chatId,
        [FromQuery] long seq,
        [FromQuery] int limit = 30,
        CancellationToken ct = default)
        => Ok(await messages.GetPageAfterAsync(chatId, User.GetUserId(), Math.Max(0, seq), Math.Clamp(limit, 1, 100), ct));

    /// <summary>
    /// Send a message.
    /// </summary>
    /// <remarks>
    /// Same rules and events as the hub method <c>SendMessage</c>: the text is trimmed
    /// and must be 1–4096 characters; chat members receive <c>MessageCreated</c> and
    /// <c>ChatListUpdated</c> over SignalR, the sender included — other devices of the
    /// sender learn about the message the same way.
    ///
    /// Pass a fresh <c>clientMessageId</c> with every message and repeat it on retries:
    /// a retry returns the already created message with <c>200</c> instead of <c>201</c>
    /// and sends no events.
    ///
    /// Errors: <c>400 MESSAGE_EMPTY</c>, <c>400 MESSAGE_TOO_LONG</c>,
    /// <c>403 NOT_A_MEMBER</c>, <c>409 CLIENT_MESSAGE_ID_CONFLICT</c> (the id is taken
    /// by a message in another chat), <c>429 RATE_LIMITED</c> (commands limit per user).
    /// </remarks>
    /// <param name="chatId">Chat ID</param>
    /// <param name="dto">Message text</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpPost("{chatId}/messages")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [RequestSizeLimit(64 * 1024)]
    [ProducesResponseType(typeof(MessageDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(MessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SendMessage(Guid chatId, [FromBody] SendMessageDto dto, CancellationToken ct)
    {
        var result = await messages.SendAsync(
            chatId, User.GetUserId(), dto.Text, dto.ClientMessageId, dto.ReplyToMessageId, dto.Entities,
            dto.AttachmentIds, ct);
        return result.Created ? Created(string.Empty, result.Message) : Ok(result.Message);
    }

    /// <summary>
    /// Forward messages from another chat (or this one).
    /// </summary>
    /// <remarks>
    /// Copies 1–100 messages of <c>fromChatId</c> into this chat in their original order.
    /// Each copy is a new message of the caller with <c>forwardFrom</c> — the original
    /// author (for a forward of a forward — the very first one). Replies are not carried over.
    /// Members of this chat receive <c>MessageCreated</c> and <c>ChatListUpdated</c> for each copy.
    ///
    /// Pass <c>clientMessageIds</c> (one per message, same order) and repeat them on retries:
    /// a retry creates nothing new and answers <c>200</c> instead of <c>201</c>.
    ///
    /// Errors: <c>400 INVALID_REQUEST</c> (empty or repeated ids, <c>clientMessageIds</c> of another
    /// length), <c>400 TOO_MANY_MESSAGES</c>, <c>403 NOT_A_MEMBER</c> (of either chat),
    /// <c>404 MESSAGE_NOT_FOUND</c> (a message is not in <c>fromChatId</c>, deleted or hidden by
    /// the caller), <c>409 CLIENT_MESSAGE_ID_CONFLICT</c>, <c>429 RATE_LIMITED</c>.
    /// </remarks>
    /// <param name="chatId">Target chat ID</param>
    /// <param name="dto">Source chat and messages</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpPost("{chatId}/messages/forward")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(typeof(ForwardMessagesResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ForwardMessagesResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ForwardMessages(Guid chatId, [FromBody] ForwardMessagesDto dto, CancellationToken ct)
    {
        var result = await messages.ForwardAsync(
            chatId, User.GetUserId(), dto.FromChatId, dto.MessageIds, dto.ClientMessageIds, ct);
        var body = new ForwardMessagesResponseDto { Items = [.. result.Messages] };
        return result.Created ? Created(string.Empty, body) : Ok(body);
    }

    /// <summary>
    /// Edit a message.
    /// </summary>
    /// <remarks>
    /// Only the author, within <c>Messages:EditWindowHours</c> (48 by default) of sending.
    /// The text follows the same rules as when sending. Members receive <c>MessageUpdated</c>
    /// with the whole message; <c>editedAt</c> is set. The same text again changes nothing
    /// and sends no event.
    ///
    /// Errors: <c>400 MESSAGE_EMPTY</c>, <c>400 MESSAGE_TOO_LONG</c>, <c>403 NOT_A_MEMBER</c>,
    /// <c>403 NOT_MESSAGE_AUTHOR</c>, <c>403 EDIT_WINDOW_EXPIRED</c>,
    /// <c>404 MESSAGE_NOT_FOUND</c> (not in this chat or deleted), <c>429 RATE_LIMITED</c>.
    /// </remarks>
    /// <param name="chatId">Chat ID</param>
    /// <param name="messageId">Message ID</param>
    /// <param name="dto">New text</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpPatch("{chatId}/messages/{messageId}")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [RequestSizeLimit(64 * 1024)]
    [ProducesResponseType(typeof(MessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> EditMessage(
        Guid chatId, Guid messageId, [FromBody] EditMessageDto dto, CancellationToken ct)
        => Ok(await messages.EditAsync(chatId, User.GetUserId(), messageId, dto.Text, dto.Entities, ct));

    /// <summary>
    /// Delete a message for everyone or only for yourself.
    /// </summary>
    /// <remarks>
    /// <c>forEveryone=true</c> — only the author, within <c>Messages:DeleteWindowHours</c>
    /// (48 by default). The message disappears from history, search and the chat list for
    /// all members; they receive <c>MessageDeleted</c> with <c>forEveryone: true</c>.
    ///
    /// Without it — any member, any message, any time: the message disappears only for the
    /// caller; their devices receive <c>MessageDeleted</c> with <c>forEveryone: false</c>.
    ///
    /// Repeating a delete returns <c>204</c> and sends nothing.
    ///
    /// Errors: <c>403 NOT_A_MEMBER</c>, <c>403 NOT_MESSAGE_AUTHOR</c>,
    /// <c>403 DELETE_WINDOW_EXPIRED</c>, <c>404 MESSAGE_NOT_FOUND</c>, <c>429 RATE_LIMITED</c>.
    /// </remarks>
    /// <param name="chatId">Chat ID</param>
    /// <param name="messageId">Message ID</param>
    /// <param name="forEveryone">Delete for all members, not only for yourself.</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpDelete("{chatId}/messages/{messageId}")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> DeleteMessage(
        Guid chatId, Guid messageId, [FromQuery] bool forEveryone = false, CancellationToken ct = default)
    {
        await messages.DeleteAsync(chatId, User.GetUserId(), messageId, forEveryone, ct);
        return NoContent();
    }

    /// <summary>
    /// Put a reaction on a message.
    /// </summary>
    /// <remarks>
    /// One reaction per user per message: a new one replaces the previous. The emoji must be one
    /// of the instance's set (<c>Messages:Reactions</c>). Members receive <c>ReactionsChanged</c>;
    /// the same reaction again changes nothing and sends no event.
    ///
    /// Errors: <c>400 INVALID_REACTION</c>, <c>403 NOT_A_MEMBER</c>,
    /// <c>404 MESSAGE_NOT_FOUND</c> (not in this chat, deleted, or hidden by the caller),
    /// <c>429 RATE_LIMITED</c>.
    /// </remarks>
    /// <param name="chatId">Chat ID</param>
    /// <param name="messageId">Message ID</param>
    /// <param name="dto">The reaction</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpPut("{chatId}/messages/{messageId}/reactions")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(typeof(MessageReactionsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SetReaction(
        Guid chatId, Guid messageId, [FromBody] SetReactionDto dto, CancellationToken ct)
        => Ok(await reactions.SetAsync(chatId, User.GetUserId(), messageId, dto.Emoji, ct));

    /// <summary>
    /// Remove your reaction from a message.
    /// </summary>
    /// <remarks>
    /// Members receive <c>ReactionsChanged</c> with <c>emoji: null</c>. Without a reaction —
    /// <c>204</c> and no event. Errors as for putting one.
    /// </remarks>
    /// <param name="chatId">Chat ID</param>
    /// <param name="messageId">Message ID</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpDelete("{chatId}/messages/{messageId}/reactions")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RemoveReaction(Guid chatId, Guid messageId, CancellationToken ct)
    {
        await reactions.RemoveAsync(chatId, User.GetUserId(), messageId, ct);
        return NoContent();
    }

    /// <summary>
    /// Save the draft of this chat.
    /// </summary>
    /// <remarks>
    /// One draft per user and chat, shared by the user's devices: they receive <c>DraftUpdated</c>,
    /// and a new device finds the draft in <c>GET /api/chats</c> (<c>draft</c>). Save with a pause
    /// of a second or two after typing stops, not on every key. The text is kept as typed (not
    /// trimmed); formatting and the reply follow the rules of sending. Text of only spaces without
    /// a reply removes the draft — <c>204</c>. Sending a message to the chat removes it too.
    /// The same draft again changes nothing and sends nothing.
    ///
    /// Errors: <c>400 MESSAGE_TOO_LONG</c>, <c>400 INVALID_ENTITIES</c>, <c>400 REPLY_TARGET_NOT_FOUND</c>,
    /// <c>403 NOT_A_MEMBER</c>, <c>429 RATE_LIMITED</c>.
    /// </remarks>
    /// <param name="chatId">Chat ID</param>
    /// <param name="dto">The draft</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpPut("{chatId}/draft")]
    [EnableRateLimiting(ServiceExtensions.ComposerRateLimitPolicy)]
    [RequestSizeLimit(64 * 1024)]
    [ProducesResponseType(typeof(DraftDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SaveDraft(Guid chatId, [FromBody] SaveDraftDto dto, CancellationToken ct)
    {
        var draft = await drafts.SaveAsync(chatId, User.GetUserId(), dto.Text, dto.Entities, dto.ReplyToMessageId, ct);
        return draft is null ? NoContent() : Ok(draft);
    }

    /// <summary>
    /// Remove the draft of this chat.
    /// </summary>
    /// <remarks>
    /// The user's devices receive <c>DraftUpdated</c> with <c>draft: null</c>. Without a draft —
    /// <c>204</c> and no event. Errors: <c>403 NOT_A_MEMBER</c>, <c>429 RATE_LIMITED</c>.
    /// </remarks>
    /// <param name="chatId">Chat ID</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpDelete("{chatId}/draft")]
    [EnableRateLimiting(ServiceExtensions.ComposerRateLimitPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> DeleteDraft(Guid chatId, CancellationToken ct)
    {
        await drafts.DeleteAsync(chatId, User.GetUserId(), ct);
        return NoContent();
    }

    /// <summary>
    /// Report typing.
    /// </summary>
    /// <remarks>
    /// Same as the hub method <c>Typing</c>: other members receive <c>TypingChanged</c>.
    /// "Typing" expires after 6 seconds unless repeated, so send <c>isTyping: true</c>
    /// every few seconds while the user types and <c>false</c> when they stop.
    /// </remarks>
    /// <param name="chatId">Chat ID</param>
    /// <param name="dto">Typing state</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpPost("{chatId}/typing")]
    [EnableRateLimiting(ServiceExtensions.ComposerRateLimitPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Typing(Guid chatId, [FromBody] TypingDto dto, CancellationToken ct)
    {
        await presence.SetTypingAsync(chatId, User.GetUserId(), dto.IsTyping, ct);
        return NoContent();
    }

    /// <summary>
    /// Mark messages as read
    /// </summary>
    /// <remarks>
    /// Moves the caller's read pointer to this message (forward only; an older one is not an
    /// error) and clears "marked as unread". Authors whose messages first became read receive
    /// <c>MessagesRead</c>; the caller's devices receive <c>ReadStateChanged</c> with the new counters.
    ///
    /// Errors: <c>403 NOT_A_MEMBER</c>, <c>404 MESSAGE_NOT_FOUND</c> (not in this chat).
    /// </remarks>
    [HttpPost("{chatId}/read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead(Guid chatId, [FromBody] MarkMessageReadDto dto, CancellationToken ct)
    {
        await readState.MarkReadAsync(chatId, User.GetUserId(), dto.LastMessageId, ct);
        return Ok();
    }

    /// <summary>
    /// Mark the chat as unread, or remove the mark.
    /// </summary>
    /// <remarks>
    /// A reminder for the user only: nobody else sees it, the unread counter does not change.
    /// Reading the chat (<c>POST /read</c>) removes it. The caller's devices receive
    /// <c>ReadStateChanged</c>; setting what is already set changes nothing and sends nothing.
    ///
    /// Errors: <c>403 NOT_A_MEMBER</c>, <c>429 RATE_LIMITED</c>.
    /// </remarks>
    /// <param name="chatId">Chat ID</param>
    /// <param name="dto">The mark</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpPut("{chatId}/marked-unread")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SetMarkedUnread(Guid chatId, [FromBody] MarkUnreadDto dto, CancellationToken ct)
    {
        await readState.SetMarkedUnreadAsync(chatId, User.GetUserId(), dto.MarkedUnread, ct);
        return NoContent();
    }

    /// <summary>
    /// The chat's gallery: photos and videos, files, voice messages or links.
    /// </summary>
    /// <remarks>
    /// Messages of the chat that carry files of the kind (<c>media</c> — photos and videos,
    /// <c>files</c>, <c>voice</c>) or web links (<c>links</c> — an address in the text or a link in
    /// the formatting), **newest first**, as a gallery shows them. The same <c>MessageDto</c> as the
    /// history, with <c>attachments</c>; previews come from <c>POST /api/media/links</c>. What the
    /// caller deleted for themselves is left out.
    ///
    /// Errors: <c>400 INVALID_FILTER</c>, <c>400 INVALID_CURSOR</c>, <c>403 NOT_A_MEMBER</c>.
    /// </remarks>
    /// <param name="chatId">Chat ID.</param>
    /// <param name="filter"><c>media</c>, <c>files</c>, <c>voice</c> or <c>links</c>.</param>
    /// <param name="cursor">From the previous page; omit for the newest.</param>
    /// <param name="limit">Messages per page (default 50, max 100).</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpGet("{chatId}/media")]
    [ProducesResponseType(typeof(CursorPaginatedResponse<MessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetGallery(
        Guid chatId, [FromQuery] string? filter, [FromQuery] string? cursor, [FromQuery] int limit = 50,
        CancellationToken ct = default)
        => Ok(await messages.GetGalleryAsync(chatId, User.GetUserId(), filter, cursor, Math.Clamp(limit, 1, 100), ct));

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
    /// <param name="ct">Request cancellation.</param>
    [HttpGet("{chatId}/messages/search")]
    [ProducesResponseType(typeof(SearchMessagesResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SearchMessages(
        Guid chatId,
        [FromQuery] string q,
        [FromQuery] string? cursor,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
        => Ok(await messages.SearchAsync(chatId, User.GetUserId(), q, cursor, Math.Clamp(limit, 1, 100), ct));
}
