using BasicApi.Middleware.Exceptions;
using BasicApi.Models;
using BasicApi.Models.Dto.Message;
using BasicApi.Services.Events;
using BasicApi.Storage;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Exceptions;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Services;

public interface IMessageService
{
    /// <summary>A history page: newest to oldest by cursor, within a page - in order (seq).</summary>
    Task<CursorPaginatedResponse<MessageDto>> GetPageAsync(
        Guid chatId, Guid userId, string? cursor, int limit, CancellationToken ct = default);

    /// <summary>
    /// "Jump to date": a page that ends with the last message
    /// at that moment or earlier; further into the past - via <c>nextCursor</c>.
    /// </summary>
    Task<CursorPaginatedResponse<MessageDto>> GetPageAtAsync(
        Guid chatId, Guid userId, DateTime date, int limit, CancellationToken ct = default);

    /// <summary>Full-text search in a chat with cursor pagination.</summary>
    Task<SearchMessagesResponseDto> SearchAsync(
        Guid chatId, Guid userId, string query, string? cursor, int limit, CancellationToken ct = default);

    /// <summary>
    /// Send: the text is trimmed at the edges and validated; an event goes to the members.
    /// With <paramref name="clientMessageId"/> sending is idempotent: a repeat returns the already
    /// created message (<c>Created = false</c>) and broadcasts nothing.
    /// Errors: 400 <c>MESSAGE_EMPTY</c>/<c>MESSAGE_TOO_LONG</c>, 403 <c>NOT_A_MEMBER</c>,
    /// 409 <c>CLIENT_MESSAGE_ID_CONFLICT</c> - this id is already taken by a message in another chat.
    /// </summary>
    Task<SendResult> SendAsync(
        Guid chatId, Guid senderId, string? text, Guid? clientMessageId = null, CancellationToken ct = default);

    /// <summary>
    /// Moves the read pointer forward. A message not from this chat - 404
    /// <c>MESSAGE_NOT_FOUND</c>; an attempt to move it back is not an error, nothing changes.
    /// </summary>
    Task MarkReadAsync(Guid chatId, Guid userId, Guid messageId, CancellationToken ct = default);
}

/// <param name="Message">The message - new or found by clientMessageId.</param>
/// <param name="Created">false - this is a repeat of an already performed send.</param>
public sealed record SendResult(MessageDto Message, bool Created);

public sealed class MessageService(
    IDbSession db,
    IMessageRepository messageRepository,
    IMembershipService membership,
    IChatPolicy policy,
    IChatEventPublisher events) : IMessageService
{
    public async Task<CursorPaginatedResponse<MessageDto>> GetPageAsync(
        Guid chatId, Guid userId, string? cursor, int limit, CancellationToken ct = default)
    {
        var parsed = ParseCursor(cursor);
        await policy.DemandReadAsync(userId, chatId, ct);

        return await PageAsync(chatId, await ResolveCursorAsync(chatId, parsed, ct), limit, ct);
    }

    public async Task<CursorPaginatedResponse<MessageDto>> GetPageAtAsync(
        Guid chatId, Guid userId, DateTime date, int limit, CancellationToken ct = default)
    {
        // Access check - before any queries for the chat's messages.
        await policy.DemandReadAsync(userId, chatId, ct);

        // A date with any offset (...Z, ...+03:00) is one and the same moment; in the DB it is UTC.
        var utcDate = date.Kind switch
        {
            DateTimeKind.Local => date.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(date, DateTimeKind.Utc),
            _ => date
        };

        // The cursor is exclusive, so we take the first message AFTER the date: the page before
        // it ends with the last message up to and including the date. If there are no messages after
        // the date, it is simply the last page.
        var firstAfter = await messageRepository.GetFirstSeqAfterAsync(chatId, utcDate, ct);
        return await PageAsync(chatId, firstAfter, limit, ct);
    }

    private async Task<CursorPaginatedResponse<MessageDto>> PageAsync(
        Guid chatId, long? beforeSeq, int limit, CancellationToken ct)
    {
        var result = await messageRepository.GetMessagesWithSenderCursorAsync(chatId, beforeSeq, limit, ct);
        var messages = result.Items.Select(Map).ToList();

        return new CursorPaginatedResponse<MessageDto>
        {
            Items = [.. messages.OrderBy(m => m.Seq)],
            NextCursor = NextCursor(messages, result.HasMore),
            HasMore = result.HasMore
        };
    }

    public async Task<SearchMessagesResponseDto> SearchAsync(
        Guid chatId, Guid userId, string query, string? cursor, int limit, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            throw new BadRequestException("Query must be at least 2 characters long", "INVALID_QUERY");

        var parsed = ParseCursor(cursor);
        await policy.DemandReadAsync(userId, chatId, ct);

        var beforeSeq = await ResolveCursorAsync(chatId, parsed, ct);
        var (result, totalCount) = await messageRepository.SearchMessagesCursorAsync(chatId, query, beforeSeq, limit, ct);
        var messages = result.Items.Select(Map).ToList();

        return new SearchMessagesResponseDto
        {
            Items = [.. messages.OrderBy(m => m.Seq)],
            NextCursor = NextCursor(messages, result.HasMore),
            HasMore = result.HasMore,
            Query = query,
            TotalCount = totalCount
        };
    }

    public async Task<SendResult> SendAsync(
        Guid chatId, Guid senderId, string? text, Guid? clientMessageId = null, CancellationToken ct = default)
    {
        // Validate the text before going to the DB: it is free.
        var textError = MessageText.Normalize(text, out var normalized);
        if (textError == MessageText.EmptyCode)
            throw new BadRequestException("Message text is empty", textError);
        if (textError == MessageText.TooLongCode)
            throw new BadRequestException($"Message text is longer than {MessageText.MaxLength} characters", textError);

        await policy.DemandPostAsync(senderId, chatId, ct);

        // A repeated send (retry after a network drop) - the same message, without a second event.
        if (clientMessageId is { } retryId &&
            await messageRepository.GetByClientMessageIdAsync(senderId, retryId, ct) is { } sent)
        {
            return AlreadySent(sent, chatId);
        }

        var memberIds = await membership.GetMemberIdsAsync(chatId, ct);

        try
        {
            // The message and its event are one transaction: one saved means the other is saved too.
            var message = await db.InTransactionAsync(async ct =>
            {
                var created = Map(await messageRepository.CreateAsync(new Message
                {
                    Id = Guid.NewGuid(),
                    ChatId = chatId,
                    SenderId = senderId,
                    Text = normalized,
                    CreatedAt = DateTime.UtcNow,
                    IsDeleted = false
                }, clientMessageId, ct));

                await events.MessageCreatedAsync(created, memberIds, ct);
                return created;
            }, ct: ct);

            return new SendResult(message, Created: true);
        }
        catch (DuplicateKeyException)
        {
            // A parallel repeat of the same send got there first - return its result.
            var winner = await messageRepository.GetByClientMessageIdAsync(senderId, clientMessageId!.Value, CancellationToken.None)
                ?? throw new InvalidOperationException("Duplicate clientMessageId, but the message is not found");
            return AlreadySent(winner, chatId);
        }
    }

    private static SendResult AlreadySent(MessageWithSender sent, Guid chatId) =>
        sent.ChatId == chatId
            ? new SendResult(Map(sent), Created: false)
            : throw new ConflictException(
                "clientMessageId is already used by a message in another chat", "CLIENT_MESSAGE_ID_CONFLICT");

    public async Task MarkReadAsync(Guid chatId, Guid userId, Guid messageId, CancellationToken ct = default)
    {
        await policy.DemandReadAsync(userId, chatId, ct);

        // A message from another chat - 404: otherwise the pointer could land on someone else's
        // message, and the unread counter would break.
        var update = await messageRepository.MarkReadAsync(chatId, userId, messageId, ct);
        if (update == ReadPointerUpdate.MessageNotFound)
            throw new NotFoundException("Message not found in this chat", "MESSAGE_NOT_FOUND");
    }

    private static MessageDto Map(MessageWithSender m) => new()
    {
        Id = m.Id,
        ChatId = m.ChatId,
        SenderId = m.SenderId,
        SenderName = m.SenderName,
        Text = m.Text,
        CreatedAt = m.CreatedAt,
        IsRead = false, // TODO: read status by the companion - plan 2
        Seq = m.Seq,
        ClientMessageId = m.ClientMessageId
    };

    /// <summary>
    /// A cursor to the next (older) page - from the last message of the page
    /// (the page came newest to oldest). No next page - no cursor either.
    /// </summary>
    private static string? NextCursor(List<MessageDto> newestFirst, bool hasMore) =>
        hasMore && newestFirst.Count > 0 ? MessageCursor.BeforeSeqOf(newestFirst[^1].Seq).Encode() : null;

    /// <summary>The cursor format is validated before anything else: a malformed cursor is 400.</summary>
    private static MessageCursor? ParseCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
            return null;
        return MessageCursor.TryDecode(cursor, out var parsed) ? parsed : throw InvalidCursor();
    }

    /// <summary>A cursor in the old format points to a message by id - take its seq.</summary>
    private async Task<long?> ResolveCursorAsync(Guid chatId, MessageCursor? cursor, CancellationToken ct)
    {
        if (cursor is not { } c)
            return null;
        if (c.BeforeSeq is { } seq)
            return seq;
        return await messageRepository.GetSeqAsync(chatId, c.LegacyMessageId!.Value, ct) ?? throw InvalidCursor();
    }

    private static BadRequestException InvalidCursor() => new("Cursor is malformed", "INVALID_CURSOR");
}
