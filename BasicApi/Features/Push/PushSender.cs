using System.Collections.Concurrent;
using System.Text.Json;
using BasicApi.Models.Dto.Message;
using BasicApi.Services;
using BasicApi.Services.Events;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Features.Push;

/// <summary>
/// Sends push notifications of new messages (and of reactions, to the message's author) to members
/// who are offline, except the sender and those who muted the chat or blocked the sender; online
/// members get the event over the hub.
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

        var targets = await devices.GetPushTargetsAsync(job.ChatId, job.SenderId, job.RecipientId, ct);
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
        // A newer notification of the chat replaces one the device has not received yet; a reaction
        // replaces only an older reaction to the same message, never a message.
        var topic = (notification.Kind == PushNotificationDto.ReactionKind ? notification.MessageId : job.ChatId).ToString("N");

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
