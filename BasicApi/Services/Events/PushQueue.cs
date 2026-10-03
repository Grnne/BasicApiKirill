using System.Threading.Channels;
using BasicApi.Models.Dto.Message;

namespace BasicApi.Services.Events;

/// <summary>
/// A new message, or a reaction, that may need push notifications; <paramref name="RecipientId"/> —
/// only to that member (the author of the message reacted to), otherwise to every member.
/// </summary>
public sealed record PushJob(Guid ChatId, Guid SenderId, PushNotificationDto Notification, Guid? RecipientId = null);

/// <summary>
/// Messages waiting for their push notifications, filled by the outbox dispatcher. In memory: a
/// notification lost on a restart is not worth a table — the message itself is in the journal
/// and the client gets it on the next <c>/sync</c>.
/// </summary>
public sealed class PushQueue
{
    public const int Capacity = 10_000;

    private readonly Channel<PushJob> _jobs;

    public PushQueue(bool enabled, ILogger<PushQueue> logger)
    {
        Enabled = enabled;
        _jobs = Channel.CreateBounded<PushJob>(
            new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
            dropped => logger.LogWarning("Push queue is full: notifications of message {MessageId} dropped",
                dropped.Notification.MessageId));
    }

    /// <summary>Push is configured; otherwise nothing is queued.</summary>
    public bool Enabled { get; }

    public ChannelReader<PushJob> Reader => _jobs.Reader;

    public void Enqueue(PushJob job)
    {
        if (Enabled)
            _jobs.Writer.TryWrite(job);
    }
}

public static class PushNotifications
{
    /// <summary>The notification of a new message; the chat's type and title are added on sending.</summary>
    public static PushNotificationDto Of(MessageDto message) => new()
    {
        ChatId = message.ChatId,
        MessageId = message.Id,
        Seq = message.Seq,
        SenderId = message.SenderId,
        SenderName = message.SenderName,
        MessageType = message.Type,
        Text = SignalRChatEventPublisher.Preview(message).Text,
        AttachmentKind = message.Attachments.FirstOrDefault()?.Kind,
        AttachmentCount = message.Attachments.Count,
        CreatedAt = message.CreatedAt
    };

    /// <summary>
    /// The notification of a reaction to <paramref name="message"/>: the sender is who reacted,
    /// the text is the message's.
    /// </summary>
    public static PushNotificationDto OfReaction(MessageDto message, Guid reactorId, string reactorName, string emoji, DateTime at)
    {
        var notification = Of(message);
        notification.Kind = PushNotificationDto.ReactionKind;
        notification.SenderId = reactorId;
        notification.SenderName = reactorName;
        notification.Emoji = emoji;
        notification.CreatedAt = at;
        return notification;
    }
}
