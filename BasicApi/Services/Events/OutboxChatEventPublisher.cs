using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Users;
using BasicApi.Models.Dto.Message;
using BasicApi.Storage;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Services.Events;

/// <summary>
/// Events via the outbox: written into the current transaction together with the change that
/// caused them, and into the recipients' change journal (for /sync). They are dispatched by
/// <see cref="OutboxDispatcher"/> in the previous format. "Typing" and online are ephemeral:
/// they go out immediately and are not written to the journal.
/// </summary>
public sealed class OutboxChatEventPublisher(
    IDbSession db,
    IOutboxRepository outbox,
    IUpdateJournal journal,
    OutboxSignal signal,
    SignalRChatEventPublisher live) : IChatEventPublisher
{
    public Task MessageCreatedAsync(
        MessageDto message, IReadOnlyCollection<Guid> memberIds, CancellationToken ct = default) =>
        db.InTransactionAsync(async ct =>
        {
            await journal.AppendAsync(memberIds, UpdateTypes.MessageCreated, Json(message), ct);
            await EnqueueAsync(UpdateTypes.MessageCreated, ct,
                HubSend.Group(message.ChatId, "MessageCreated", message),
                HubSend.Users(memberIds, "ChatListUpdated", message.ChatId, SignalRChatEventPublisher.Preview(message)));
            return true;
        }, ct: ct);

    public Task MessageUpdatedAsync(
        MessageDto message, IReadOnlyCollection<Guid> memberIds, CancellationToken ct = default) =>
        ToAllAsync(UpdateTypes.MessageUpdated, message, memberIds, ct);

    public Task MessageDeletedAsync(
        MessageDeletedDto deleted, IReadOnlyCollection<Guid> recipientIds, CancellationToken ct = default) =>
        ToAllAsync(UpdateTypes.MessageDeleted, deleted, recipientIds, ct);

    public Task ReactionsChangedAsync(
        MessageReactionsDto reactions, IReadOnlyCollection<Guid> memberIds, CancellationToken ct = default) =>
        ToAllAsync(UpdateTypes.ReactionsChanged, reactions, memberIds, ct);

    public Task MessagesDeliveredAsync(
        ReceiptDto receipt, IReadOnlyCollection<Guid> authorIds, CancellationToken ct = default) =>
        ToAllAsync(UpdateTypes.MessagesDelivered, receipt, authorIds, ct);

    public Task MessagesReadAsync(
        ReceiptDto receipt, IReadOnlyCollection<Guid> authorIds, CancellationToken ct = default) =>
        ToAllAsync(UpdateTypes.MessagesRead, receipt, authorIds, ct);

    public Task ReadStateChangedAsync(ReadStateDto state, Guid userId, CancellationToken ct = default) =>
        ToAllAsync(UpdateTypes.ReadStateChanged, state, [userId], ct);

    public Task DraftUpdatedAsync(DraftUpdatedDto draft, Guid userId, CancellationToken ct = default) =>
        ToAllAsync(UpdateTypes.DraftUpdated, draft, [userId], ct);

    public Task MemberUpdatedAsync(MemberUpdatedDto update, IReadOnlyCollection<Guid> memberIds, CancellationToken ct = default) =>
        ToAllAsync(UpdateTypes.MemberUpdated, update, memberIds, ct);

    public Task MembersAddedAsync(MembersAddedDto added, IReadOnlyCollection<Guid> recipientIds, CancellationToken ct = default) =>
        ToAllAsync(UpdateTypes.MemberAdded, added, recipientIds, ct);

    public Task MemberRemovedAsync(
        MemberRemovedDto removed, IReadOnlyCollection<Guid> recipientIds, CancellationToken ct = default) =>
        db.InTransactionAsync(async ct =>
        {
            await journal.AppendAsync(recipientIds, UpdateTypes.MemberRemoved, Json(removed), ct);
            // Out of the chat's group first: nothing of the chat reaches them after this point.
            await EnqueueAsync(UpdateTypes.MemberRemoved, ct,
                HubSend.LeaveGroup([removed.UserId], removed.ChatId),
                HubSend.Users(recipientIds, UpdateTypes.MemberRemoved, removed));
            return true;
        }, ct: ct);

    public Task ChatUpdatedAsync(ChatUpdatedDto update, IReadOnlyCollection<Guid> memberIds, CancellationToken ct = default) =>
        ToAllAsync(UpdateTypes.ChatUpdated, update, memberIds, ct);

    public Task BlockListChangedAsync(BlockListChangedDto change, Guid userId, CancellationToken ct = default) =>
        ToAllAsync(UpdateTypes.BlockListChanged, change, [userId], ct);

    public Task PrivacyUpdatedAsync(PrivacySettingsDto settings, Guid userId, CancellationToken ct = default) =>
        ToAllAsync(UpdateTypes.PrivacyUpdated, settings, [userId], ct);

    public Task UserUpdatedAsync(UserUpdatedDto update, IReadOnlyCollection<Guid> recipientIds, CancellationToken ct = default) =>
        ToAllAsync(UpdateTypes.UserUpdated, update, recipientIds, ct);

    public Task ChatDeletedAsync(ChatDeletedDto deleted, IReadOnlyCollection<Guid> memberIds, CancellationToken ct = default) =>
        db.InTransactionAsync(async ct =>
        {
            await journal.AppendAsync(memberIds, UpdateTypes.ChatDeleted, Json(deleted), ct);
            await EnqueueAsync(UpdateTypes.ChatDeleted, ct,
                HubSend.LeaveGroup(memberIds, deleted.ChatId),
                HubSend.Users(memberIds, UpdateTypes.ChatDeleted, deleted));
            return true;
        }, ct: ct);

    /// <summary>
    /// The same payload to the journal and to every connection of the recipients — not just the
    /// open chat: a client updates the chat list preview from it too.
    /// </summary>
    private Task ToAllAsync<T>(string type, T payload, IReadOnlyCollection<Guid> recipientIds, CancellationToken ct) =>
        db.InTransactionAsync(async ct =>
        {
            await journal.AppendAsync(recipientIds, type, Json(payload), ct);
            await EnqueueAsync(type, ct, HubSend.Users(recipientIds, type, payload));
            return true;
        }, ct: ct);

    public Task ChatCreatedAsync(Guid recipientId, ChatListItemDto item, bool live = true, CancellationToken ct = default) =>
        db.InTransactionAsync(async ct =>
        {
            await journal.AppendAsync([recipientId], UpdateTypes.ChatCreated, Json(item), ct);
            if (live)
                await EnqueueAsync(UpdateTypes.ChatCreated, ct, HubSend.User(recipientId, "ChatCreated", item));
            return true;
        }, ct: ct);

    public Task ChatCreatedAsync(IReadOnlyCollection<Guid> recipientIds, ChatListItemDto item, CancellationToken ct = default) =>
        ToAllAsync(UpdateTypes.ChatCreated, item, recipientIds, ct);

    public Task UserOnlineChangedAsync(
        Guid userId, bool isOnline, IReadOnlyCollection<Guid> recipientIds, CancellationToken ct = default) =>
        live.UserOnlineChangedAsync(userId, isOnline, recipientIds, ct);

    public Task TypingChangedAsync(
        Guid chatId, Guid userId, bool isTyping, IReadOnlyCollection<Guid> recipientIds, CancellationToken ct = default) =>
        live.TypingChangedAsync(chatId, userId, isTyping, recipientIds, ct);

    private async Task EnqueueAsync(string type, CancellationToken ct, params HubSend[] sends)
    {
        await outbox.EnqueueAsync(type, new OutboxEnvelope(sends).Serialize(), ct);
        // The dispatcher will see the event only after the commit — so we wake it then.
        db.OnCommitted(signal.Notify);
    }

    private static string Json<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value, OutboxEnvelope.Json);
}

/// <summary>Journal entry types — the same names as the hub events.</summary>
public static class UpdateTypes
{
    public const string MessageCreated = "MessageCreated";
    public const string MessageUpdated = "MessageUpdated";
    public const string MessageDeleted = "MessageDeleted";
    public const string ReactionsChanged = "ReactionsChanged";
    public const string MessagesDelivered = "MessagesDelivered";
    public const string MessagesRead = "MessagesRead";
    public const string ReadStateChanged = "ReadStateChanged";
    public const string DraftUpdated = "DraftUpdated";
    public const string MemberUpdated = "MemberUpdated";
    public const string MemberAdded = "MemberAdded";
    public const string MemberRemoved = "MemberRemoved";
    public const string ChatUpdated = "ChatUpdated";
    public const string ChatDeleted = "ChatDeleted";
    public const string ChatCreated = "ChatCreated";
    public const string UserUpdated = "UserUpdated";
    public const string PrivacyUpdated = "PrivacyUpdated";
    public const string BlockListChanged = "BlockListChanged";
}
