using System.Text.Json;
using BasicApi.Hubs;
using BasicApi.Models.Dto.Push;
using BasicApi.Services.Push;
using BasicApi.Storage;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace BasicApi.Services.Events;

/// <summary>Dispatcher alarm: an event is committed — dispatch it without waiting for the poll.</summary>
public sealed class OutboxSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        try { _signal.Release(); }
        catch (SemaphoreFullException) { } // already woken
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);
}

/// <summary>
/// Dispatches events from the outbox in order. Woken after every commit that has an event,
/// and in case of a missed signal (or an event from another instance) it also
/// polls the outbox every <c>Outbox:PollIntervalMs</c>.
///
/// "At least once" delivery: if the process crashes between dispatch and marking, the event
/// is sent again — clients recognize a repeat by the message or chat id.
/// </summary>
public sealed class OutboxDispatcher(
    IServiceScopeFactory scopes,
    IHubContext<ChatHub> hub,
    HubConnectionRegistry connections,
    OutboxSignal signal,
    PushQueue pushes,
    IConfiguration configuration,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    public const int BatchSize = 100;

    /// <summary>After this many failed attempts the event is taken off dispatch (and logged).</summary>
    public const int MaxAttempts = 10;

    private readonly TimeSpan _pollInterval =
        TimeSpan.FromMilliseconds(configuration.GetValue("Outbox:PollIntervalMs", 1000));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Outbox:DispatcherEnabled", true))
        {
            logger.LogWarning("Outbox dispatcher is disabled: events are stored but not sent");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                DispatchResult result;
                do result = await DispatchPendingAsync(stoppingToken);
                while (result is { Sent: > 0, Failed: false });
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The database is unavailable, etc. — we'll try on the next round.
                logger.LogError(ex, "Outbox dispatch failed");
            }

            try
            {
                await signal.WaitAsync(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Dispatches one batch of undispatched events. Stops at the first failure
    /// to keep the order: the event will be retried on the next round.
    /// </summary>
    public async Task<DispatchResult> DispatchPendingAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbSession>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();

        return await db.InTransactionAsync(async ct =>
        {
            var rows = await outbox.LockPendingAsync(BatchSize, ct);
            var sent = new List<long>(rows.Count);

            foreach (var row in rows)
            {
                try
                {
                    await SendAsync(row, ct);
                    sent.Add(row.Id);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await outbox.MarkProcessedAsync(sent, ct);
                    var attempts = await outbox.MarkFailedAsync(row.Id, MaxAttempts, ct);
                    if (attempts >= MaxAttempts)
                        logger.LogError(ex, "Outbox event {Id} ({Type}) dropped after {Attempts} attempts", row.Id, row.Type, attempts);
                    else
                        logger.LogWarning(ex, "Outbox event {Id} ({Type}) failed, attempt {Attempts}", row.Id, row.Type, attempts);
                    return new DispatchResult(sent.Count, Failed: true);
                }
            }

            await outbox.MarkProcessedAsync(sent, ct);
            return new DispatchResult(sent.Count, Failed: false);
        }, ct: ct);
    }

    private async Task SendAsync(OutboxRow row, CancellationToken ct)
    {
        foreach (var send in OutboxEnvelope.Deserialize(row.Payload).Sends)
        {
            if (send.Target == "push")
            {
                // Slow and unreliable push services must not hold up the events behind this one.
                pushes.Enqueue(new PushJob(Guid.Parse(send.Ids[0]), Guid.Parse(send.Ids[1]),
                    send.Args[0].Deserialize<PushNotificationDto>(OutboxEnvelope.Json)!));
                continue;
            }

            if (send.Target == "leave-group")
            {
                await connections.RemoveFromGroupAsync(hub.Groups, send.Ids.Select(Guid.Parse), send.Method, ct);
                continue;
            }

            IClientProxy target = send.Target switch
            {
                "group" => hub.Clients.Group(send.Ids[0]),
                "user" => hub.Clients.User(send.Ids[0]),
                "users" => hub.Clients.Users(send.Ids),
                _ => throw new InvalidOperationException($"Unknown outbox target '{send.Target}'")
            };
            await target.SendCoreAsync(send.Method, [.. send.Args.Select(a => (object?)a)], ct);
        }
    }
}

/// <param name="Sent">How many events were dispatched.</param>
/// <param name="Failed">There was a failure — the batch was interrupted.</param>
public sealed record DispatchResult(int Sent, bool Failed);
