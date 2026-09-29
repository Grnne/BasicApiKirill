using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;

namespace BasicApi.Storage.Interfaces;

public interface IMessageRepository
{
    /// <summary>
    /// A page of messages with sender names, newest first by seq: strictly before
    /// <paramref name="beforeSeq"/>, or the latest when it is null. Skips messages deleted for
    /// everyone and those <paramref name="viewerId"/> deleted for themselves.
    /// </summary>
    Task<CursorResult<MessageWithSender>> GetMessagesWithSenderCursorAsync(
        Guid chatId, Guid viewerId, long? beforeSeq, int limit, CancellationToken ct = default);

    /// <summary>
    /// Seq of the oldest message strictly after the given moment (UTC), or null.
    /// "Jump to date" uses it as an exclusive cursor so the page ends at the date.
    /// </summary>
    Task<long?> GetFirstSeqAfterAsync(Guid chatId, DateTime date, CancellationToken ct = default);

    /// <summary>Seq of a message of this chat; null when there is no such message in it.</summary>
    Task<long?> GetSeqAsync(Guid chatId, Guid messageId, CancellationToken ct = default);

    /// <summary>
    /// Full-text search within a chat, newest first by seq, before <paramref name="beforeSeq"/>
    /// when given. Also returns the total number of matches. Skips what the viewer does not see.
    /// </summary>
    Task<(CursorResult<MessageWithSender> Result, int TotalCount)> SearchMessagesCursorAsync(
        Guid chatId, Guid viewerId, string query, long? beforeSeq, int limit, CancellationToken ct = default);

    /// <summary>
    /// Inserts a message with the next seq of its chat; returns it with the sender's display name.
    /// Throws <see cref="Exceptions.DuplicateKeyException"/> when the sender already has a message
    /// with this <paramref name="clientMessageId"/> (a concurrent retry won).
    /// </summary>
    Task<MessageWithSender> CreateAsync(Message message, Guid? clientMessageId = null, CancellationToken ct = default);

    /// <summary>The sender's message with this client-chosen id, or null.</summary>
    Task<MessageWithSender?> GetByClientMessageIdAsync(Guid senderId, Guid clientMessageId, CancellationToken ct = default);

    /// <summary>A message of this chat, tombstones included; null when there is no such message in it.</summary>
    Task<MessageWithSender?> GetAsync(Guid chatId, Guid messageId, CancellationToken ct = default);

    /// <summary>
    /// The given messages of this chat that the viewer sees (not deleted, not hidden by them),
    /// in seq order. Missing ones are simply absent.
    /// </summary>
    Task<IReadOnlyList<MessageWithSender>> GetVisibleAsync(
        Guid chatId, Guid viewerId, IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default);

    /// <summary>Replaces the text and its formatting, sets edited_at; null when the message is deleted.</summary>
    Task<MessageWithSender?> EditTextAsync(
        Guid messageId, string text, string? entitiesJson, DateTime editedAt, CancellationToken ct = default);

    /// <summary>Replaces who is mentioned in the message; an empty list clears it.</summary>
    Task SetMentionsAsync(Guid messageId, Guid chatId, long seq, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);

    /// <summary>Turns the message into a tombstone; false when it already is one.</summary>
    Task<bool> DeleteForEveryoneAsync(Guid messageId, DateTime deletedAt, CancellationToken ct = default);

    /// <summary>Hides the message from this user only; false when it is already hidden.</summary>
    Task<bool> HideAsync(Guid userId, Guid messageId, CancellationToken ct = default);

    /// <summary>
    /// Moves the member's read pointer to the given message — forward only, by seq.
    /// A message from another chat (or a non-existent one) is rejected without
    /// touching the pointer.
    /// </summary>
    Task<ReadPointerUpdate> MarkReadAsync(Guid chatId, Guid userId, Guid messageId, CancellationToken ct = default);
}
