using BasicApi.Hubs;
using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;
using Microsoft.AspNetCore.SignalR;

namespace BasicApi.Services.Events;

/// <summary>
/// Broadcasts events through SignalR immediately, in the former format: event names and arguments
/// are the same as the client received from the hub. For ephemeral events; the rest go through
/// <see cref="OutboxChatEventPublisher"/>.
/// </summary>
public sealed class SignalRChatEventPublisher(IHubContext<ChatHub> hub) : IChatEventPublisher
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

    public Task ChatCreatedAsync(Guid recipientId, ChatListItemDto item, bool live = true, CancellationToken ct = default) =>
        live ? hub.Clients.User(recipientId.ToString()).SendAsync("ChatCreated", item, ct) : Task.CompletedTask;

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

    /// <summary>Message for a chat list row: the text is truncated.</summary>
    public static MessageDto Preview(MessageDto message) => new()
    {
        Id = message.Id,
        ChatId = message.ChatId,
        SenderId = message.SenderId,
        SenderName = message.SenderName,
        Text = message.Text.Length > PreviewLength ? message.Text[..PreviewLength] + "…" : message.Text,
        CreatedAt = message.CreatedAt,
        IsRead = message.IsRead
    };

    private static string[] ToStrings(IReadOnlyCollection<Guid> ids) => [.. ids.Select(id => id.ToString())];
}
