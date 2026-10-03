using BasicApi.Hubs;
using BasicApi.Models;
using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Users;
using BasicApi.Models.Dto.Message;
using Microsoft.AspNetCore.SignalR;

namespace BasicApi.Services.Events;

/// <summary>
/// Broadcasts events through SignalR immediately; used for ephemeral events, the rest go through
/// <see cref="OutboxChatEventPublisher"/>.
/// </summary>
public sealed class SignalRChatEventPublisher(IHubContext<ChatHub> hub, HubConnectionRegistry connections) : IChatEventPublisher
{
    /// <summary>Text length in the chat list preview.</summary>
    public const int PreviewLength = 100;

    public async Task MessageCreatedAsync(
        MessageDto message, IReadOnlyCollection<Guid> memberIds, CancellationToken ct = default)
    {
        await hub.Clients.Group(message.ChatId.ToString()).SendAsync("MessageCreated", message, ct);

        if (memberIds.Count > 0)
            await hub.Clients.Users(ToStrings(memberIds)).SendAsync("ChatListUpdated", message.ChatId, Preview(message), ct);
    }

    public Task MessageJournaledAsync(
        MessageDto message, IReadOnlyCollection<Guid> recipientIds, CancellationToken ct = default) =>
        Task.CompletedTask; // there is no journal here

    public Task MessageUpdatedAsync(
        MessageDto message, IReadOnlyCollection<Guid> memberIds, CancellationToken ct = default) =>
        memberIds.Count == 0
            ? Task.CompletedTask
            : hub.Clients.Users(ToStrings(memberIds)).SendAsync("MessageUpdated", message, ct);

    public Task MessageDeletedAsync(
        MessageDeletedDto deleted, IReadOnlyCollection<Guid> recipientIds, CancellationToken ct = default) =>
        recipientIds.Count == 0
            ? Task.CompletedTask
            : hub.Clients.Users(ToStrings(recipientIds)).SendAsync("MessageDeleted", deleted, ct);

    public Task ReactionsChangedAsync(
        MessageReactionsDto reactions, IReadOnlyCollection<Guid> memberIds, CancellationToken ct = default) =>
        memberIds.Count == 0
            ? Task.CompletedTask
            : hub.Clients.Users(ToStrings(memberIds)).SendAsync("ReactionsChanged", reactions, ct);

    public Task ReactionAddedAsync(PushNotificationDto notification, Guid authorId, CancellationToken ct = default) =>
        hub.Clients.User(authorId.ToString()).SendAsync("Notification", notification, ct); // push goes through the outbox only

    public Task MessagesDeliveredAsync(
        ReceiptDto receipt, IReadOnlyCollection<Guid> authorIds, CancellationToken ct = default) =>
        ToUsers(authorIds, "MessagesDelivered", receipt, ct);

    public Task MessagesReadAsync(
        ReceiptDto receipt, IReadOnlyCollection<Guid> authorIds, CancellationToken ct = default) =>
        ToUsers(authorIds, "MessagesRead", receipt, ct);

    public Task ReadStateChangedAsync(ReadStateDto state, Guid userId, CancellationToken ct = default) =>
        hub.Clients.User(userId.ToString()).SendAsync("ReadStateChanged", state, ct);

    public Task DraftUpdatedAsync(DraftUpdatedDto draft, Guid userId, CancellationToken ct = default) =>
        hub.Clients.User(userId.ToString()).SendAsync("DraftUpdated", draft, ct);

    public Task MemberUpdatedAsync(MemberUpdatedDto update, IReadOnlyCollection<Guid> memberIds, CancellationToken ct = default) =>
        ToUsers(memberIds, "MemberUpdated", update, ct);

    public Task MembersAddedAsync(MembersAddedDto added, IReadOnlyCollection<Guid> recipientIds, CancellationToken ct = default) =>
        ToUsers(recipientIds, "MemberAdded", added, ct);

    public async Task MemberRemovedAsync(
        MemberRemovedDto removed, IReadOnlyCollection<Guid> recipientIds, CancellationToken ct = default)
    {
        await connections.RemoveFromGroupAsync(hub.Groups, [removed.UserId], removed.ChatId.ToString(), ct);
        await ToUsers(recipientIds, "MemberRemoved", removed, ct);
    }

    public Task ChatUpdatedAsync(ChatUpdatedDto update, IReadOnlyCollection<Guid> memberIds, CancellationToken ct = default) =>
        ToUsers(memberIds, "ChatUpdated", update, ct);

    public Task PinnedChatsChangedAsync(PinnedChatsDto pinned, Guid userId, CancellationToken ct = default) =>
        hub.Clients.User(userId.ToString()).SendAsync("PinnedChatsChanged", pinned, ct);

    public Task ChatStateChangedAsync(ChatStateDto state, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default) =>
        ToUsers(userIds, "ChatStateChanged", state, ct);

    public Task FoldersChangedAsync(FoldersDto folders, Guid userId, CancellationToken ct = default) =>
        hub.Clients.User(userId.ToString()).SendAsync("FoldersChanged", folders, ct);

    public Task BlockListChangedAsync(BlockListChangedDto change, Guid userId, CancellationToken ct = default) =>
        hub.Clients.User(userId.ToString()).SendAsync("BlockListChanged", change, ct);

    public Task PrivacyUpdatedAsync(PrivacySettingsDto settings, Guid userId, CancellationToken ct = default) =>
        hub.Clients.User(userId.ToString()).SendAsync("PrivacyUpdated", settings, ct);

    public Task UserUpdatedAsync(UserUpdatedDto update, IReadOnlyCollection<Guid> recipientIds, CancellationToken ct = default) =>
        ToUsers(recipientIds, "UserUpdated", update, ct);

    public async Task ChatDeletedAsync(ChatDeletedDto deleted, IReadOnlyCollection<Guid> memberIds, CancellationToken ct = default)
    {
        await connections.RemoveFromGroupAsync(hub.Groups, memberIds, deleted.ChatId.ToString(), ct);
        await ToUsers(memberIds, "ChatDeleted", deleted, ct);
    }

    public Task ChatCreatedAsync(Guid recipientId, ChatListItemDto item, bool live = true, CancellationToken ct = default) =>
        live ? hub.Clients.User(recipientId.ToString()).SendAsync("ChatCreated", item, ct) : Task.CompletedTask;

    public Task ChatCreatedAsync(IReadOnlyCollection<Guid> recipientIds, ChatListItemDto item, CancellationToken ct = default) =>
        ToUsers(recipientIds, "ChatCreated", item, ct);

    public Task UserOnlineChangedAsync(
        Guid userId, bool isOnline, IReadOnlyCollection<Guid> recipientIds, CancellationToken ct = default) =>
        recipientIds.Count == 0
            ? Task.CompletedTask
            : hub.Clients.Users(ToStrings(recipientIds)).SendAsync("UserOnlineChanged", userId, isOnline, ct);

    public Task TypingChangedAsync(
        Guid chatId, Guid userId, bool isTyping, IReadOnlyCollection<Guid> recipientIds, CancellationToken ct = default) =>
        recipientIds.Count == 0
            ? Task.CompletedTask
            : hub.Clients.Users(ToStrings(recipientIds)).SendAsync("TypingChanged", chatId, userId, isTyping, ct);

    /// <summary>
    /// Message for a chat list row: the text is truncated, formatting, replies and reactions are
    /// left out. Not a full message — clients take counters and mentions from the journal.
    /// </summary>
    public static MessageDto Preview(MessageDto message) => new()
    {
        Id = message.Id,
        ChatId = message.ChatId,
        SenderId = message.SenderId,
        SenderName = message.SenderName,
        Text = Truncate(MessageEntities.HideSpoilers(message.Text, message.Entities)),
        CreatedAt = message.CreatedAt,
        IsRead = message.IsRead,
        Seq = message.Seq,
        Type = message.Type,
        Action = message.Action,
        Attachments = message.Attachments
    };

    private static string Truncate(string text) =>
        text.Length > PreviewLength ? text[..PreviewLength] + "…" : text;

    private Task ToUsers(IReadOnlyCollection<Guid> userIds, string method, object payload, CancellationToken ct) =>
        userIds.Count == 0 ? Task.CompletedTask : hub.Clients.Users(ToStrings(userIds)).SendAsync(method, payload, ct);

    private static string[] ToStrings(IReadOnlyCollection<Guid> ids) => [.. ids.Select(id => id.ToString())];
}
