using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using BasicApi.Models.Dto.Message;
using BasicApi.Models.Dto.Push;
using BasicApi.Services.Events;
using BasicApi.Storage.Interfaces;
using Microsoft.Extensions.Options;

namespace BasicApi.Services.Push;

/// <summary>A new message that may need push notifications.</summary>
public sealed record PushJob(Guid ChatId, Guid SenderId, PushNotificationDto Notification);

/// <summary>
/// Messages waiting for their push notifications, filled by the outbox dispatcher. In memory: a
/// notification lost on a restart is not worth a table — the message itself is in the journal
/// and the client gets it on the next <c>/sync</c>.
/// </summary>
public sealed class PushQueue
{
    public const int Capacity = 10_000;

    private readonly Channel<PushJob> _jobs;

    public PushQueue(IOptions<PushOptions> options, ILogger<PushQueue> logger)
    {
        Enabled = options.Value.IsConfigured;
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
}

/// <summary>
/// Sends push notifications of new messages: to the members' subscribed devices, except the
/// sender, those who muted the chat or blocked the sender, and those who have the app open (they
/// get the message over the hub). A subscription the push service no longer knows is removed.
/// </summary>
public sealed class PushSender(
    PushQueue queue,
    IServiceScopeFactory scopes,
    IUserStatusService status,
    ILogger<PushSender> logger) : BackgroundService
{
    /// <summary>Devices notified at once: push services answer in about a hundred milliseconds.</summary>
    public const int Parallelism = 8;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!queue.Enabled)
            return;

        try
        {
            await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await SendAsync(job, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Push notifications of message {MessageId} failed", job.Notification.MessageId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public async Task SendAsync(PushJob job, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var devices = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();

        var targets = await devices.GetPushTargetsAsync(job.ChatId, job.SenderId, ct);
        if (targets.Count == 0)
            return;
        var online = await status.GetOnlineUserIdsAsync(targets.Select(t => t.UserId).ToHashSet());
        var offline = targets.Where(t => !online.Contains(t.UserId)).ToList();
        if (offline.Count == 0)
            return;

        var notification = job.Notification;
        notification.ChatType = offline[0].ChatType;
        notification.ChatTitle = offline[0].ChatTitle;
        var payload = JsonSerializer.Serialize(notification, OutboxEnvelope.Json);
        // A newer notification of the chat replaces one the device has not received yet.
        var topic = job.ChatId.ToString("N");

        var gone = new ConcurrentBag<string>();
        await Parallel.ForEachAsync(offline, new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = ct },
            async (target, ct) =>
            {
                // One transport per send: its signing key is not shared between threads.
                var transport = scope.ServiceProvider.GetRequiredService<IPushTransport>();
                if (await transport.SendAsync(target.Push, payload, topic, ct) == PushDelivery.Gone)
                    gone.Add(target.Endpoint);
            });

        foreach (var endpoint in gone)
            await devices.ClearPushByEndpointAsync(endpoint, ct);
    }
}
