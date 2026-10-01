using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Microsoft.Extensions.Options;

namespace BasicApi.Features.Media;

/// <summary>What one cleanup pass removed.</summary>
public sealed record MediaCleanupResult(int StaleUploads, int UnusedFiles, int ExpiredOriginals);

/// <summary>
/// Periodically removes unfinished uploads, files nothing points to and, with <c>Media:RetentionDays</c>,
/// originals past retention (previews stay).
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

    public async Task<MediaCleanupResult> CleanupAsync(CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var attachments = scope.ServiceProvider.GetRequiredService<IAttachmentRepository>();
        var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
        var now = time.GetUtcNow().UtcDateTime;
        var o = options.Value;

        // Unfinished uploads: objects first — a row without its object is harmless, an object
        // without a row is lost space.
        var stale = 0;
        IReadOnlyList<Attachment> batch;
        do
        {
            batch = await attachments.GetStalePendingAsync(now - TimeSpan.FromHours(o.PendingUploadHours), BatchSize, ct);
            await storage.DeleteAsync(batch.SelectMany(a =>
                new[] { a.StorageKey, MediaService.ClientThumbnailKey(a.Id), MediaService.ThumbnailKey(a.Id) }), ct);
            stale += await attachments.DeleteAsync([.. batch.Select(a => a.Id)], ct);
        }
        while (batch.Count == BatchSize);

        // Unused files: the row first — its deletion is what checks that nothing points to it.
        var unused = 0;
        do
        {
            batch = await attachments.DeleteUnreferencedAsync(now - TimeSpan.FromHours(o.UnusedFileHours), BatchSize, ct);
            await storage.DeleteAsync(batch.SelectMany(Keys), ct);
            unused += batch.Count;
        }
        while (batch.Count == BatchSize);

        var expired = 0;
        if (o.RetentionDays > 0)
        {
            do
            {
                batch = await attachments.GetExpiringAsync(now - TimeSpan.FromDays(o.RetentionDays), BatchSize, ct);
                await storage.DeleteAsync(batch.Select(a => a.StorageKey), ct);
                expired += await attachments.MarkExpiredAsync([.. batch.Select(a => a.Id)], ct);
            }
            while (batch.Count == BatchSize);
        }

        if (stale + unused + expired > 0)
            logger.LogInformation(
                "Media cleanup: {Stale} stale uploads, {Unused} unused files removed, {Expired} originals expired",
                stale, unused, expired);
        return new MediaCleanupResult(stale, unused, expired);
    }

    private static IEnumerable<string> Keys(Attachment a) =>
        a.ThumbnailKey is null ? [a.StorageKey] : [a.StorageKey, a.ThumbnailKey];

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
