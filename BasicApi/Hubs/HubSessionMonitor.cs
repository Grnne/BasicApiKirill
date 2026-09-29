using BasicApi.Services;

namespace BasicApi.Hubs;

/// <summary>
/// Раз в <c>Hub:SessionCheckIntervalSeconds</c> (60 с) проверяет входы открытых соединений
/// хаба и обрывает соединения закончившихся: цепочка отозвана при повторе refresh-токена
/// или истекла. Logout и logout-all обрывают соединения сразу, без этой проверки.
///
/// Соединение с токеном без входа (<c>sid</c>) живёт до истечения токена.
/// </summary>
public sealed class HubSessionMonitor(
    HubConnectionRegistry connections,
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<HubSessionMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(configuration.GetValue("Hub:SessionCheckIntervalSeconds", 60));
        using var timer = new PeriodicTimer(interval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await CheckAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Hub session check failed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Одна проверка; возвращает, сколько соединений оборвано.</summary>
    public async Task<int> CheckAsync(CancellationToken ct = default)
    {
        var aborted = connections.AbortExpiredWithoutSession(time.GetUtcNow());

        // Проверяем только входы из этого снимка: соединение, открытое после него,
        // не должно оборваться лишь потому, что его вход не попал в запрос.
        var families = connections.SessionFamilies();
        if (families.Count > 0)
        {
            await using var scope = scopes.CreateAsyncScope();
            var sessions = scope.ServiceProvider.GetRequiredService<ISessionService>();
            var live = await sessions.GetLiveSessionFamiliesAsync(families, ct);
            aborted += connections.AbortSessionFamilies(families.Except(live).ToHashSet());
        }

        if (aborted > 0)
            logger.LogInformation("Closed {Count} hub connections of ended sessions", aborted);
        return aborted;
    }
}
