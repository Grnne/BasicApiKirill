using BasicApi.Hubs;
using BasicApi.Storage;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace BasicApi.Services.Events;

/// <summary>Будильник диспетчера: событие закоммичено — рассылай, не дожидаясь опроса.</summary>
public sealed class OutboxSignal
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        try { _signal.Release(); }
        catch (SemaphoreFullException) { } // уже разбужен
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);
}

/// <summary>
/// Рассылает события из outbox по порядку. Будится после каждого коммита с событием,
/// а на случай пропущенного сигнала (или события от другого экземпляра) ещё и
/// опрашивает outbox раз в <c>Outbox:PollIntervalMs</c>.
///
/// Доставка «хотя бы раз»: если процесс упадёт между рассылкой и отметкой, событие
/// уйдёт повторно — клиенты узнают повтор по id сообщения или чата.
/// </summary>
public sealed class OutboxDispatcher(
    IServiceScopeFactory scopes,
    IHubContext<ChatHub> hub,
    OutboxSignal signal,
    IConfiguration configuration,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    public const int BatchSize = 100;

    /// <summary>После стольких неудачных попыток событие снимается с рассылки (и пишется в лог).</summary>
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
                // База недоступна и т.п. — попробуем на следующем круге.
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
    /// Рассылает одну пачку неразосланных событий. На первой неудаче останавливается,
    /// чтобы не нарушить порядок: событие повторится на следующем круге.
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

/// <param name="Sent">Сколько событий разослано.</param>
/// <param name="Failed">Была неудача — пачка прервана.</param>
public sealed record DispatchResult(int Sent, bool Failed);
