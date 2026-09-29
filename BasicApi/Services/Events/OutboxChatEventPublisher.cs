using BasicApi.Models.Dto.Chat;
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

    public Task ChatCreatedAsync(Guid recipientId, ChatListItemDto item, bool live = true, CancellationToken ct = default) =>
        db.InTransactionAsync(async ct =>
        {
            await journal.AppendAsync([recipientId], UpdateTypes.ChatCreated, Json(item), ct);
            if (live)
                await EnqueueAsync(UpdateTypes.ChatCreated, ct, HubSend.User(recipientId, "ChatCreated", item));
            return true;
        }, ct: ct);

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
    public const string ChatCreated = "ChatCreated";
}
