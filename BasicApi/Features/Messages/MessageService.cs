using BasicApi.Middleware.Exceptions;
using BasicApi.Models;
using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;
using BasicApi.Services;
using BasicApi.Services.Events;
using BasicApi.Storage;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Exceptions;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Features.Messages;

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

    /// <summary>
    /// The chat's gallery, newest first: <c>media</c> (photos and videos), <c>files</c>,
    /// <c>voice</c> or <c>links</c>. Errors: 400 <c>INVALID_FILTER</c>/<c>INVALID_CURSOR</c>,
    /// 403 <c>NOT_A_MEMBER</c>.
    /// </summary>
    Task<CursorPaginatedResponse<MessageDto>> GetGalleryAsync(
        Guid chatId, Guid userId, string? filter, string? cursor, int limit, CancellationToken ct = default);

    /// <summary>
    /// Search across all the user's chats (D10), newest first, with filters. Errors: 400
    /// <c>INVALID_QUERY</c>/<c>INVALID_CURSOR</c>/<c>INVALID_FILTER</c>, 403 <c>NOT_A_MEMBER</c> (the chat filter).
    /// </summary>
    Task<GlobalSearchResponseDto> SearchAllAsync(
        Guid userId, string? query, MessageSearchFilter filter, string? cursor, int limit, CancellationToken ct = default);

    /// <summary>Full-text search in a chat with cursor pagination.</summary>
    Task<SearchMessagesResponseDto> SearchAsync(
        Guid chatId, Guid userId, string query, string? cursor, int limit, CancellationToken ct = default);

    /// <summary>
    /// Send: the text is trimmed at the edges and validated; an event goes to the members.
    /// With <paramref name="clientMessageId"/> sending is idempotent: a repeat returns the already
    /// created message (<c>Created = false</c>) and broadcasts nothing.
    /// Mentioned members get the message counted in their unread mentions. With
    /// <paramref name="attachmentIds"/> it is a media message (an album) and the text is its caption.
    /// Errors: 400 <c>MESSAGE_EMPTY</c>/<c>MESSAGE_TOO_LONG</c>/<c>REPLY_TARGET_NOT_FOUND</c>/
    /// <c>INVALID_ENTITIES</c>/<c>INVALID_ALBUM</c>, 403 <c>NOT_A_MEMBER</c>/<c>PERMISSION_DENIED</c>,
    /// 404 <c>ATTACHMENT_NOT_FOUND</c>,
    /// 409 <c>CLIENT_MESSAGE_ID_CONFLICT</c> - this id is already taken by a message in another chat.
    /// </summary>
    Task<SendResult> SendAsync(
        Guid chatId, Guid senderId, string? text, Guid? clientMessageId = null, Guid? replyToMessageId = null,
        IReadOnlyList<MessageEntityDto>? entities = null, IReadOnlyList<Guid>? attachmentIds = null,
        CancellationToken ct = default);

    /// <summary>
    /// Copies messages of <paramref name="fromChatId"/> into <paramref name="chatId"/> in their
    /// original order, each with a link to its original author; members get <c>MessageCreated</c>
    /// for each. With <paramref name="clientMessageIds"/> a retry returns the copies made before.
    /// Errors: 400 <c>INVALID_REQUEST</c>/<c>TOO_MANY_MESSAGES</c>, 403 <c>NOT_A_MEMBER</c>,
    /// 404 <c>MESSAGE_NOT_FOUND</c> - a message is not in the source chat or not visible to the user,
    /// 409 <c>CLIENT_MESSAGE_ID_CONFLICT</c>.
    /// </summary>
    Task<ForwardResult> ForwardAsync(
        Guid chatId, Guid userId, Guid fromChatId, IReadOnlyList<Guid> messageIds,
        IReadOnlyList<Guid>? clientMessageIds = null, CancellationToken ct = default);

    /// <summary>
    /// Replaces the text and its formatting; members get <c>MessageUpdated</c>. The same text and
    /// formatting is not an edit: the message comes back unchanged and nothing is sent.
    /// Errors: 400 <c>MESSAGE_EMPTY</c>/<c>MESSAGE_TOO_LONG</c>/<c>INVALID_ENTITIES</c>,
    /// 403 <c>NOT_A_MEMBER</c>/<c>NOT_MESSAGE_AUTHOR</c>/<c>EDIT_WINDOW_EXPIRED</c>/
    /// <c>MESSAGE_NOT_EDITABLE</c>, 404 <c>MESSAGE_NOT_FOUND</c>.
    /// </summary>
    Task<MessageDto> EditAsync(
        Guid chatId, Guid userId, Guid messageId, string? text, IReadOnlyList<MessageEntityDto>? entities = null,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes for everyone (a tombstone; all members get <c>MessageDeleted</c>) or only for the
    /// user (their own devices get it). Repeating a delete is not an error.
    /// Errors: 403 <c>NOT_A_MEMBER</c>/<c>NOT_MESSAGE_AUTHOR</c>/<c>DELETE_WINDOW_EXPIRED</c>,
    /// 404 <c>MESSAGE_NOT_FOUND</c>.
    /// </summary>
    Task DeleteAsync(Guid chatId, Guid userId, Guid messageId, bool forEveryone, CancellationToken ct = default);
}

/// <param name="Message">The message - new or found by clientMessageId.</param>
/// <param name="Created">false - this is a repeat of an already performed send.</param>
public sealed record SendResult(MessageDto Message, bool Created);

/// <param name="Messages">The copies in the target chat, in order.</param>
/// <param name="Created">false - all of them had been made by an earlier attempt.</param>
public sealed record ForwardResult(IReadOnlyList<MessageDto> Messages, bool Created);

public sealed class MessageService(
    IDbSession db,
    IMessageRepository messageRepository,
    IMembershipService membership,
    IChatPolicy policy,
    IChatEventPublisher events,
    IDraftRepository drafts,
    IGroupRepository groups,
    IAttachmentRepository attachments,
    IChatStateService? chatStates = null) : IMessageService
{
    /// <summary>How many messages one forward may carry.</summary>
    public const int MaxForward = 100;

    public async Task<CursorPaginatedResponse<MessageDto>> GetPageAsync(
        Guid chatId, Guid userId, string? cursor, int limit, CancellationToken ct = default)
    {
        var parsed = ParseCursor(cursor);
        await policy.DemandReadAsync(userId, chatId, ct);

        return await PageAsync(chatId, userId, await ResolveCursorAsync(chatId, parsed, ct), limit, ct);
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
        return await PageAsync(chatId, userId, firstAfter, limit, ct);
    }

    private async Task<CursorPaginatedResponse<MessageDto>> PageAsync(
        Guid chatId, Guid viewerId, long? beforeSeq, int limit, CancellationToken ct)
    {
        var result = await messageRepository.GetMessagesWithSenderCursorAsync(chatId, viewerId, beforeSeq, limit, ct);
        var messages = await MapForViewerAsync(result.Items, chatId, viewerId, ct);

        return new CursorPaginatedResponse<MessageDto>
        {
            Items = [.. messages.OrderBy(m => m.Seq)],
            NextCursor = NextCursor(messages, result.HasMore),
            HasMore = result.HasMore
        };
    }

    public async Task<CursorPaginatedResponse<MessageDto>> GetGalleryAsync(
        Guid chatId, Guid userId, string? filter, string? cursor, int limit, CancellationToken ct = default)
    {
        string[] kinds = filter switch
        {
            "media" => [AttachmentKinds.Photo, AttachmentKinds.Video],
            "files" => [AttachmentKinds.File],
            "voice" => [AttachmentKinds.Voice],
            "links" => [],
            _ => throw new BadRequestException("filter must be media, files, voice or links", "INVALID_FILTER")
        };
        var parsed = ParseCursor(cursor);
        await policy.DemandReadAsync(userId, chatId, ct);

        var beforeSeq = await ResolveCursorAsync(chatId, parsed, ct);
        var result = await messageRepository.GetGalleryPageAsync(chatId, userId, kinds, filter == "links", beforeSeq, limit, ct);
        var messages = await MapForViewerAsync(result.Items, chatId, userId, ct);

        // Newest first, as a gallery shows them.
        return new CursorPaginatedResponse<MessageDto>
        {
            Items = messages,
            NextCursor = NextCursor(messages, result.HasMore),
            HasMore = result.HasMore
        };
    }

    public async Task<GlobalSearchResponseDto> SearchAllAsync(
        Guid userId, string? query, MessageSearchFilter filter, string? cursor, int limit, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            throw new BadRequestException("Query must be at least 2 characters long", "INVALID_QUERY");
        if (filter.Type is not (null or MessageTypes.Text or MessageTypes.Media))
            throw new BadRequestException("type must be text or media", "INVALID_FILTER");
        ChatListCursor? before = null;
        if (!string.IsNullOrEmpty(cursor))
            before = ChatListCursor.TryDecode(cursor, out var parsed) ? parsed : throw InvalidCursor();
        if (filter.ChatId is { } chatId)
            await policy.DemandReadAsync(userId, chatId, ct);
        filter.From = filter.From?.ToUniversalTime();
        filter.To = filter.To?.ToUniversalTime();

        var rows = await messageRepository.SearchAllAsync(userId, query, filter, before, limit + 1, ct);
        var page = rows.Take(limit).ToList();
        var hasMore = rows.Count > limit;
        return new GlobalSearchResponseDto
        {
            Query = query,
            HasMore = hasMore,
            NextCursor = hasMore ? new ChatListCursor(page[^1].CreatedAt, page[^1].Id).Encode() : null,
            Items = [.. page.Select(h => new GlobalSearchHitDto
            {
                Message = Map(h),
                Chat = new SearchChatDto
                {
                    ChatId = h.ChatId,
                    Type = h.ChatType,
                    Title = h.ChatTitle,
                    CompanionId = h.CompanionId,
                    CompanionName = h.CompanionName,
                    AvatarId = h.ChatAvatarId
                }
            })]
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
        var (result, totalCount) = await messageRepository.SearchMessagesCursorAsync(chatId, userId, query, beforeSeq, limit, ct);
        var messages = await MapForViewerAsync(result.Items, chatId, userId, ct);

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
        Guid chatId, Guid senderId, string? text, Guid? clientMessageId = null, Guid? replyToMessageId = null,
        IReadOnlyList<MessageEntityDto>? entities = null, IReadOnlyList<Guid>? attachmentIds = null,
        CancellationToken ct = default)
    {
        // Validate the text and its formatting before going to the DB: it is free.
        var withFiles = attachmentIds is { Count: > 0 };
        var normalized = NormalizeText(text, allowEmpty: withFiles);
        var formatting = NormalizeEntities(entities, text ?? string.Empty, normalized);
        if (withFiles && (attachmentIds!.Count > MessageAttachments.MaxPerMessage ||
                          attachmentIds.Distinct().Count() != attachmentIds.Count))
            throw new BadRequestException(
                $"attachmentIds must be 1 to {MessageAttachments.MaxPerMessage} distinct files", "INVALID_ALBUM");

        if (withFiles)
            await policy.DemandPostMediaAsync(senderId, chatId, ct);
        else
            await policy.DemandPostAsync(senderId, chatId, ct);

        // A repeated send (retry after a network drop) - the same message, without a second event.
        if (clientMessageId is { } retryId &&
            await messageRepository.GetByClientMessageIdAsync(senderId, retryId, ct) is { } sent)
        {
            return AlreadySent(sent, chatId);
        }

        // A reply to a message of another chat would leak its text into this one. System messages
        // are the server's record of the group, not something said: nobody answers them.
        if (replyToMessageId is { } replyId &&
            await messageRepository.GetAsync(chatId, replyId, ct) is not { DeletedAt: null, Type: not MessageTypes.System })
        {
            throw new BadRequestException("The message to reply to is not in this chat", "REPLY_TARGET_NOT_FOUND");
        }

        var files = withFiles ? await FilesToSendAsync(senderId, attachmentIds!, ct) : [];

        var memberIds = await membership.GetMemberIdsAsync(chatId, ct);
        var mentioned = MentionedMembers(formatting, memberIds, senderId);

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
                    Type = withFiles ? MessageTypes.Media : MessageTypes.Text,
                    ReplyToMessageId = replyToMessageId,
                    EntitiesJson = MessageEntities.Serialize(formatting)
                }, clientMessageId, ct));

                if (withFiles)
                {
                    await LinkAsync(created, [.. files.Select(f => new AttachmentRef(f.Id, f.Kind))], ct);
                    created.Attachments = [.. files.Select(MessageAttachments.ToDto)];
                }

                if (mentioned.Count > 0)
                    await messageRepository.SetMentionsAsync(created.Id, chatId, created.Seq, mentioned, ct);

                await events.MessageCreatedAsync(created, memberIds, ct);
                if (chatStates is not null)
                    await chatStates.UnarchiveOnMessageAsync(chatId, senderId, ct);

                // What was being written is sent: the draft goes, on every device of the sender.
                if (await drafts.DeleteAsync(senderId, chatId, ct))
                    await events.DraftUpdatedAsync(new DraftUpdatedDto { ChatId = chatId, Draft = null }, senderId, ct);
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

    public async Task<ForwardResult> ForwardAsync(
        Guid chatId, Guid userId, Guid fromChatId, IReadOnlyList<Guid> messageIds,
        IReadOnlyList<Guid>? clientMessageIds = null, CancellationToken ct = default)
    {
        if (messageIds.Count == 0 || messageIds.Distinct().Count() != messageIds.Count)
            throw new BadRequestException("messageIds must be a non-empty list of distinct ids", "INVALID_REQUEST");
        if (messageIds.Count > MaxForward)
            throw new BadRequestException($"At most {MaxForward} messages can be forwarded at once", "TOO_MANY_MESSAGES");
        if (clientMessageIds is not null &&
            (clientMessageIds.Count != messageIds.Count || clientMessageIds.Distinct().Count() != clientMessageIds.Count))
            throw new BadRequestException("clientMessageIds must be distinct, one per message", "INVALID_REQUEST");

        await policy.DemandReadAsync(userId, fromChatId, ct);
        await policy.DemandPostAsync(userId, chatId, ct);

        // What the user does not see (deleted, hidden, another chat) cannot be forwarded, nor can
        // the record of what happened in a group.
        var sources = await messageRepository.GetVisibleAsync(fromChatId, userId, messageIds, ct);
        if (sources.Count != messageIds.Count || sources.Any(s => s.Type == MessageTypes.System))
            throw MessageNotFound();
        // Files travel by reference, but a group may still forbid sending media.
        if (sources.Any(s => s.AttachmentsJson is not null))
            await policy.DemandPostMediaAsync(userId, chatId, ct);

        var clientIds = clientMessageIds is null
            ? null
            : messageIds.Zip(clientMessageIds).ToDictionary(p => p.First, p => p.Second);

        try
        {
            return await ForwardOnceAsync(chatId, userId, sources, clientIds, ct);
        }
        catch (DuplicateKeyException)
        {
            // A parallel retry of the same forward got there first: everything it made is found now.
            return await ForwardOnceAsync(chatId, userId, sources, clientIds, CancellationToken.None);
        }
    }

    private async Task<ForwardResult> ForwardOnceAsync(
        Guid chatId, Guid userId, IReadOnlyList<MessageWithSender> sources,
        Dictionary<Guid, Guid>? clientIds, CancellationToken ct)
    {
        // Copies made by an earlier attempt, by source message.
        var made = new Dictionary<Guid, MessageWithSender>();
        foreach (var (sourceId, clientId) in clientIds ?? new Dictionary<Guid, Guid>())
        {
            if (await messageRepository.GetByClientMessageIdAsync(userId, clientId, ct) is not { } copy)
                continue;
            if (copy.ChatId != chatId)
                throw new ConflictException(
                    "clientMessageId is already used by a message in another chat", "CLIENT_MESSAGE_ID_CONFLICT");
            made[sourceId] = copy;
        }

        var memberIds = await membership.GetMemberIdsAsync(chatId, ct);
        var items = await db.InTransactionAsync(async ct =>
        {
            var items = new List<MessageDto>(sources.Count);
            foreach (var source in sources)
            {
                if (made.TryGetValue(source.Id, out var copy))
                {
                    items.Add(Map(copy));
                    continue;
                }

                // A forward of a forward points to the very first original, as in Telegram.
                var created = Map(await messageRepository.CreateAsync(new Message
                {
                    Id = Guid.NewGuid(),
                    ChatId = chatId,
                    SenderId = userId,
                    Text = source.Text,
                    Type = source.Type,
                    // Mentions stay as formatting but notify nobody: the forwarder did not mention anyone.
                    EntitiesJson = source.EntitiesJson,
                    CreatedAt = DateTime.UtcNow,
                    ForwardFromUserId = source.IsForward ? source.ForwardFromUserId : source.SenderId,
                    ForwardFromChatId = source.IsForward ? source.ForwardFromChatId : source.ChatId,
                    ForwardFromMessageId = source.IsForward ? source.ForwardFromMessageId : source.Id
                }, clientIds?[source.Id], ct));

                // The same files, not copies: forwarding uploads nothing again.
                var files = MessageAttachments.Read(source.AttachmentsJson);
                if (files.Count > 0)
                {
                    await LinkAsync(created, [.. files.Select(f => new AttachmentRef(f.Id, f.Kind))], ct);
                    created.Attachments = files;
                }

                await events.MessageCreatedAsync(created, memberIds, ct);
                items.Add(created);
                if (chatStates is not null && items.Count == 1)
                    await chatStates.UnarchiveOnMessageAsync(chatId, userId, ct);
            }
            return items;
        }, ct: ct);

        return new ForwardResult(items, Created: made.Count < sources.Count);
    }

    public async Task<MessageDto> EditAsync(
        Guid chatId, Guid userId, Guid messageId, string? text, IReadOnlyList<MessageEntityDto>? entities = null,
        CancellationToken ct = default)
    {
        // An empty text is checked once the message is known: a caption may be removed.
        var normalized = NormalizeText(text, allowEmpty: true);
        var formatting = NormalizeEntities(entities, text ?? string.Empty, normalized);
        await policy.DemandPostAsync(userId, chatId, ct);

        var message = await FindAsync(chatId, messageId, ct);
        (await policy.CanEditMessageAsync(userId, message, ct)).Demand();
        if (normalized.Length == 0 && message.Type != MessageTypes.Media)
            throw new BadRequestException("Message text is empty", MessageText.EmptyCode);

        if (message.Text == normalized && MessageEntities.Deserialize(message.EntitiesJson).SequenceEqual(formatting))
            return Map(message);

        var memberIds = await membership.GetMemberIdsAsync(chatId, ct);
        var mentioned = MentionedMembers(formatting, memberIds, userId);
        return await db.InTransactionAsync(async ct =>
        {
            var edited = await messageRepository.EditTextAsync(
                    messageId, normalized, MessageEntities.Serialize(formatting), DateTime.UtcNow, ct)
                ?? throw MessageNotFound(); // deleted in the meantime
            // A mention added by the edit counts only if the member has not read that far yet.
            await messageRepository.SetMentionsAsync(messageId, chatId, edited.Seq, mentioned, ct);
            var dto = Map(edited);
            await events.MessageUpdatedAsync(dto, memberIds, ct);
            return dto;
        }, ct: ct);
    }

    public async Task DeleteAsync(
        Guid chatId, Guid userId, Guid messageId, bool forEveryone, CancellationToken ct = default)
    {
        await policy.DemandReadAsync(userId, chatId, ct);

        var message = await messageRepository.GetAsync(chatId, messageId, ct) ?? throw MessageNotFound();
        if (message.DeletedAt is not null)
            return; // already gone for everyone — nothing left to delete

        var deleted = new MessageDeletedDto
        {
            ChatId = chatId,
            MessageId = messageId,
            Seq = message.Seq,
            ForEveryone = forEveryone
        };

        if (forEveryone)
        {
            (await policy.CanDeleteForEveryoneAsync(userId, message, ct)).Demand();
            var memberIds = await membership.GetMemberIdsAsync(chatId, ct);
            var now = DateTime.UtcNow;
            await db.InTransactionAsync(async ct =>
            {
                if (!await messageRepository.DeleteForEveryoneAsync(messageId, now, ct))
                    return true;

                // A group admin removing what is not theirs: the other admins see it in the log.
                if (message.SenderId != userId || message.Type == MessageTypes.System)
                    await groups.AppendAuditAsync(new ChatAuditEntry
                    {
                        ChatId = chatId,
                        ActorId = userId,
                        Action = "message_deleted",
                        TargetUserId = message.SenderId,
                        DataJson = System.Text.Json.JsonSerializer.Serialize(
                            new { messageId, seq = message.Seq }, OutboxEnvelope.Json),
                        CreatedAt = now
                    }, ct);
                await events.MessageDeletedAsync(deleted, memberIds, ct);
                return true;
            }, ct: ct);
        }
        else
        {
            await db.InTransactionAsync(async ct =>
            {
                if (await messageRepository.HideAsync(userId, messageId, ct))
                    await events.MessageDeletedAsync(deleted, [userId], ct);
                return true;
            }, ct: ct);
        }
    }

    /// <summary>Messages as the viewer sees them: with the read status (D1).</summary>
    private async Task<List<MessageDto>> MapForViewerAsync(
        IEnumerable<MessageWithSender> rows, Guid chatId, Guid viewerId, CancellationToken ct)
    {
        var messages = rows.Select(Map).ToList();
        if (messages.Count > 0 && await messageRepository.GetReadPointersAsync(chatId, viewerId, ct) is { } pointers)
        {
            foreach (var message in messages)
                MessageStatuses.Apply(message, viewerId, pointers);
        }
        return messages;
    }

    /// <summary>A live message of the chat; a tombstone or a message of another chat is 404.</summary>
    private async Task<MessageWithSender> FindAsync(Guid chatId, Guid messageId, CancellationToken ct) =>
        await messageRepository.GetAsync(chatId, messageId, ct) is { DeletedAt: null } message
            ? message
            : throw MessageNotFound();

    /// <summary>
    /// The files to send, in the order given: uploaded (not pending, not expired) and visible to the
    /// sender — their own or seen in their chats — and making a valid album.
    /// </summary>
    private async Task<List<Attachment>> FilesToSendAsync(Guid senderId, IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var found = (await attachments.GetAccessibleAsync(senderId, ids, ct))
            .Where(a => a.StorageState == StorageStates.Stored)
            .ToDictionary(a => a.Id);
        if (found.Count != ids.Count)
            throw new NotFoundException("A file is not found or not available to you", "ATTACHMENT_NOT_FOUND");

        var files = ids.Select(id => found[id]).ToList();
        var kinds = files.Select(f => f.Kind).ToHashSet();
        var valid = kinds.IsSubsetOf([AttachmentKinds.Photo, AttachmentKinds.Video])
                    || kinds.SetEquals([AttachmentKinds.File])
                    || kinds.SetEquals([AttachmentKinds.Voice]) && files.Count == 1;
        return valid
            ? files
            : throw new BadRequestException(
                "An album is photos and videos, or files only; a voice message goes alone", "INVALID_ALBUM");
    }

    /// <summary>Puts the files into the new message; one swept by the cleanup meanwhile undoes the send.</summary>
    private async Task LinkAsync(MessageDto message, IReadOnlyList<AttachmentRef> files, CancellationToken ct)
    {
        if (await attachments.LinkToMessageAsync(message.Id, message.ChatId, message.Seq, files, ct) != files.Count)
            throw new NotFoundException("A file is not found or not available to you", "ATTACHMENT_NOT_FOUND");
    }

    private static string NormalizeText(string? text, bool allowEmpty = false)
    {
        var error = MessageText.Normalize(text, out var normalized);
        if (error == MessageText.EmptyCode && allowEmpty)
            return string.Empty;
        if (error == MessageText.EmptyCode)
            throw new BadRequestException("Message text is empty", error);
        if (error == MessageText.TooLongCode)
            throw new BadRequestException($"Message text is longer than {MessageText.MaxLength} characters", error);
        return normalized;
    }

    private static List<MessageEntityDto> NormalizeEntities(
        IReadOnlyList<MessageEntityDto>? entities, string raw, string normalized) =>
        MessageEntities.Normalize(entities, raw, normalized, out var result) is { } error
            ? throw new BadRequestException(error, MessageEntities.InvalidCode)
            : result;

    /// <summary>
    /// Who gets the mention counted: members of the chat except the author. Mentioning someone
    /// outside the chat is an error — the server does not reveal non-members through a mention.
    /// </summary>
    private static IReadOnlyList<Guid> MentionedMembers(
        List<MessageEntityDto> formatting, IReadOnlyCollection<Guid> memberIds, Guid authorId)
    {
        var mentioned = MessageEntities.MentionedUsers(formatting);
        if (mentioned.Any(id => !memberIds.Contains(id)))
            throw new BadRequestException("A mentioned user is not a member of this chat", MessageEntities.InvalidCode);
        return [.. mentioned.Where(id => id != authorId)];
    }

    private static NotFoundException MessageNotFound() => new("Message not found in this chat", "MESSAGE_NOT_FOUND");

    private static MessageDto Map(MessageWithSender m) => MessageMapper.Map(m);

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
