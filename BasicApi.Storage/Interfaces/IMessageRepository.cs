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
    /// The chat's gallery: messages the viewer sees that carry files of the given kinds, or —
    /// with <paramref name="links"/> — web links; newest first, before <paramref name="beforeSeq"/>.
    /// </summary>
    Task<CursorResult<MessageWithSender>> GetGalleryPageAsync(
        Guid chatId, Guid viewerId, IReadOnlyCollection<string> kinds, bool links, long? beforeSeq, int limit,
        CancellationToken ct = default);

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
    /// touching the pointer. What is read is delivered too: the delivery pointer catches up.
    /// </summary>
    Task<ReadPointerMove> MarkReadAsync(Guid chatId, Guid userId, Guid messageId, CancellationToken ct = default);

    /// <summary>The member's own read pointer and how far the others got; null for a non-member.</summary>
    Task<ReadPointers?> GetReadPointersAsync(Guid chatId, Guid viewerId, CancellationToken ct = default);

    /// <summary>
    /// Authors whose messages first got the status when <paramref name="memberId"/>'s pointer of
    /// this kind moved from <paramref name="fromSeq"/> to <paramref name="toSeq"/>: they have a
    /// message in that range that no other member had reached yet. The member's own messages
    /// do not count.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetAuthorsNewlyReachedAsync(
        Guid chatId, Guid memberId, long fromSeq, long toSeq, ReceiptKind kind, CancellationToken ct = default);
}
