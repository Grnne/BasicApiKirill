using BasicApi.Storage.Interfaces;

namespace BasicApi.Services.Events;

/// <summary>
/// Чистка раз в <c>Sync:CleanupIntervalMinutes</c>: журнал изменений хранится
/// <c>Sync:RetentionDays</c> (30 дней), разосланные события outbox — неделю.
/// Удаляет пачками, чтобы не держать долгих блокировок.
/// </summary>
public sealed class JournalCleanup(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<JournalCleanup> logger) : BackgroundService
{
    public const int BatchSize = 5000;
    public static readonly TimeSpan OutboxRetention = TimeSpan.FromDays(7);

    public TimeSpan JournalRetention { get; } = TimeSpan.FromDays(configuration.GetValue("Sync:RetentionDays", 30));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(configuration.GetValue("Sync:CleanupIntervalMinutes", 60));
        using var timer = new PeriodicTimer(interval, time);
        do
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Journal cleanup failed");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    /// <summary>Одна чистка; возвращает, сколько удалено записей журнала и событий outbox.</summary>
    public async Task<(int Updates, int Events)> CleanupAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var journal = scope.ServiceProvider.GetRequiredService<IUpdateJournal>();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var now = time.GetUtcNow().UtcDateTime;

        int updates = 0, events = 0, deleted;
        do updates += deleted = await journal.DeleteOlderThanAsync(now - JournalRetention, BatchSize, ct);
        while (deleted == BatchSize);
        do events += deleted = await outbox.DeleteProcessedOlderThanAsync(now - OutboxRetention, BatchSize, ct);
        while (deleted == BatchSize);

        if (updates + events > 0)
            logger.LogInformation("Journal cleanup: {Updates} updates, {Events} outbox events deleted", updates, events);
        return (updates, events);
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
