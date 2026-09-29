using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;

namespace BasicApi.Services.Events;

/// <summary>
/// Events for clients. Domain services report what happened and do not know
/// how it is delivered: hub event names, groups and the payload format are
/// the implementation's concern.
/// </summary>
public interface IChatEventPublisher
{
    /// <summary>
    /// New message: <c>MessageCreated</c> to open chats (hub group) and
    /// <c>ChatListUpdated</c> with a preview to all participants.
    /// </summary>
    Task MessageCreatedAsync(MessageDto message, IReadOnlyCollection<Guid> memberIds, CancellationToken ct = default);

    /// <summary><c>MessageUpdated</c> with the whole message to all participants.</summary>
    Task MessageUpdatedAsync(MessageDto message, IReadOnlyCollection<Guid> memberIds, CancellationToken ct = default);

    /// <summary>
    /// <c>MessageDeleted</c>: to all participants when deleted for everyone, to the user's own
    /// devices when deleted for themselves.
    /// </summary>
    Task MessageDeletedAsync(MessageDeletedDto deleted, IReadOnlyCollection<Guid> recipientIds, CancellationToken ct = default);

    /// <summary><c>ReactionsChanged</c> with the message's reactions to all participants.</summary>
    Task ReactionsChangedAsync(MessageReactionsDto reactions, IReadOnlyCollection<Guid> memberIds, CancellationToken ct = default);

    /// <summary><c>MessagesDelivered</c> to the authors whose messages the member's device received.</summary>
    Task MessagesDeliveredAsync(ReceiptDto receipt, IReadOnlyCollection<Guid> authorIds, CancellationToken ct = default);

    /// <summary><c>MessagesRead</c> to the authors whose messages the member read.</summary>
    Task MessagesReadAsync(ReceiptDto receipt, IReadOnlyCollection<Guid> authorIds, CancellationToken ct = default);

    /// <summary><c>ReadStateChanged</c> to the user's own devices.</summary>
    Task ReadStateChangedAsync(ReadStateDto state, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// A new chat for a user; the card is built for them. <paramref name="live"/> —
    /// whether to send <c>ChatCreated</c> right away: the chat creator does not get it, they got the card
    /// in the response, and their other devices learn about the chat through sync.
    /// </summary>
    Task ChatCreatedAsync(Guid recipientId, ChatListItemDto item, bool live = true, CancellationToken ct = default);

    /// <summary><c>UserOnlineChanged</c> is an ephemeral event, not written to the journal.</summary>
    Task UserOnlineChangedAsync(
        Guid userId, bool isOnline, IReadOnlyCollection<Guid> recipientIds, CancellationToken ct = default);

    /// <summary><c>TypingChanged</c> is an ephemeral event, not written to the journal.</summary>
    Task TypingChangedAsync(
        Guid chatId, Guid userId, bool isTyping, IReadOnlyCollection<Guid> recipientIds, CancellationToken ct = default);
}
