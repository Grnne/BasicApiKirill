using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;
using BasicApi.Services;
using BasicApi.Services.Events;
using BasicApi.Storage;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Features.Messages;

/// <summary>How far a member has read a chat: the read pointer and the "marked as unread" flag.</summary>
public interface IReadStateService
{
    /// <summary>
    /// Moves the read pointer forward and clears "marked as unread". Authors whose messages first
    /// became read get <c>MessagesRead</c>; the user's devices get <c>ReadStateChanged</c> when
    /// anything changed. A message not from this chat - 404 <c>MESSAGE_NOT_FOUND</c>; an attempt to
    /// move the pointer back is not an error. Errors: 403 <c>NOT_A_MEMBER</c>.
    /// </summary>
    Task MarkReadAsync(Guid chatId, Guid userId, Guid messageId, CancellationToken ct = default);

    /// <summary>
    /// Sets or removes the user's "marked as unread" on the chat; the user's devices get
    /// <c>ReadStateChanged</c> when it changed. Errors: 403 <c>NOT_A_MEMBER</c>.
    /// </summary>
    Task SetMarkedUnreadAsync(Guid chatId, Guid userId, bool markedUnread, CancellationToken ct = default);
}

public sealed class ReadStateService(
    IDbSession db,
    IMessageRepository messages,
    IChatRepository chats,
    IChatPolicy policy,
    IChatEventPublisher events) : IReadStateService
{
    public async Task MarkReadAsync(Guid chatId, Guid userId, Guid messageId, CancellationToken ct = default)
    {
        await policy.DemandReadAsync(userId, chatId, ct);

        await db.InTransactionAsync(async ct =>
        {
            // A message from another chat - 404: otherwise the pointer could land on someone else's
            // message, and the unread counter would break.
            var move = await messages.MarkReadAsync(chatId, userId, messageId, ct);
            if (move.Update == ReadPointerUpdate.MessageNotFound)
                throw new NotFoundException("Message not found in this chat", "MESSAGE_NOT_FOUND");

            if (move.Update == ReadPointerUpdate.Moved)
            {
                // Only authors whose messages nobody had read yet: the others already show "read".
                var authors = await messages.GetAuthorsNewlyReachedAsync(
                    chatId, userId, move.FromSeq, move.ToSeq, ReceiptKind.Read, ct);
                if (authors.Count > 0)
                    await events.MessagesReadAsync(
                        new ReceiptDto { ChatId = chatId, UserId = userId, Seq = move.ToSeq }, authors, ct);
            }

            if (move.Update == ReadPointerUpdate.Moved || move.ClearedMark)
                await PublishStateAsync(chatId, userId, ct);
            return true;
        }, ct: ct);
    }

    public async Task SetMarkedUnreadAsync(Guid chatId, Guid userId, bool markedUnread, CancellationToken ct = default)
    {
        await policy.DemandReadAsync(userId, chatId, ct);

        await db.InTransactionAsync(async ct =>
        {
            if (await chats.SetMarkedUnreadAsync(chatId, userId, markedUnread, ct))
                await PublishStateAsync(chatId, userId, ct);
            return true;
        }, ct: ct);
    }

    /// <summary>The counters as they are after the change, from the same query as the chat list.</summary>
    private async Task PublishStateAsync(Guid chatId, Guid userId, CancellationToken ct)
    {
        if (await chats.GetChatListItemAsync(chatId, userId, ct) is not { } row)
            return;

        await events.ReadStateChangedAsync(new ReadStateDto
        {
            ChatId = chatId,
            LastReadSeq = row.LastReadSeq,
            UnreadCount = row.UnreadCount,
            UnreadMentionCount = row.UnreadMentionCount,
            MarkedUnread = row.MarkedUnread
        }, userId, ct);
    }
}
