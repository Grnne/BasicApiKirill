using BasicApi.Features.Auth;

namespace BasicApi.Hubs;

/// <summary>
/// Every <c>Hub:SessionCheckIntervalSeconds</c> (60 s) drops hub connections whose sign-in has ended
/// (chain revoked on refresh-token reuse, or expired); logout drops them at once without it.
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

    /// <summary>One check; returns how many connections were dropped.</summary>
    public async Task<int> CheckAsync(CancellationToken ct = default)
    {
        var aborted = connections.AbortExpiredWithoutSession(time.GetUtcNow());

        // We check only the sign-ins from this snapshot: a connection opened after it
        // must not be dropped just because its sign-in did not make it into the query.
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
