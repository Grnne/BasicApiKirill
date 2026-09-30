using BasicApi.Storage.Interfaces;
using Microsoft.Extensions.Options;

namespace BasicApi.Services.Media;

/// <summary>
/// Sweep every <c>Media:CleanupIntervalMinutes</c>: uploads not completed within
/// <c>Media:PendingUploadHours</c> are removed with their objects.
/// </summary>
public sealed class MediaCleanup(
    IServiceScopeFactory scopes,
    IOptions<MediaOptions> options,
    IOptions<StorageOptions> storageOptions,
    TimeProvider time,
    ILogger<MediaCleanup> logger) : BackgroundService
{
    public const int BatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!storageOptions.Value.IsConfigured)
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.Value.CleanupIntervalMinutes), time);
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
                logger.LogError(ex, "Media cleanup failed");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    /// <summary>One pass; returns how many stale uploads were removed.</summary>
    public async Task<int> CleanupAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var attachments = scope.ServiceProvider.GetRequiredService<IAttachmentRepository>();
        var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
        var startedBefore = time.GetUtcNow().UtcDateTime - TimeSpan.FromHours(options.Value.PendingUploadHours);

        var removed = 0;
        IReadOnlyList<Storage.Entities.Attachment> stale;
        do
        {
            stale = await attachments.GetStalePendingAsync(startedBefore, BatchSize, ct);
            // Objects first: a row without its object is harmless, an object without a row is lost space.
            await storage.DeleteAsync(stale.SelectMany(a =>
                new[] { a.StorageKey, MediaService.ClientThumbnailKey(a.Id), MediaService.ThumbnailKey(a.Id) }), ct);
            removed += await attachments.DeleteAsync([.. stale.Select(a => a.Id)], ct);
        }
        while (stale.Count == BatchSize);

        if (removed > 0)
            logger.LogInformation("Media cleanup: {Uploads} stale uploads removed", removed);
        return removed;
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
