using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Microsoft.Extensions.Options;

namespace BasicApi.Features.Media;

/// <summary>What one cleanup pass removed.</summary>
public sealed record MediaCleanupResult(int StaleUploads, int UnusedFiles, int ExpiredOriginals, int UploadLeftovers = 0);

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

    /// <summary>A link checked at the start of a request may still be writing a little after it expires.</summary>
    private static readonly TimeSpan LinkMargin = TimeSpan.FromMinutes(10);

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
            await storage.DeleteAsync(batch.SelectMany(a => new[]
            {
                a.StorageKey, MediaService.UploadKey(a.Id), MediaService.ClientThumbnailKey(a.Id), MediaService.ThumbnailKey(a.Id)
            }), ct);
            stale += await attachments.DeleteAsync([.. batch.Select(a => a.Id)], ct);
        }
        while (batch.Count == BatchSize);

        // What links wrote that no completion took (a second upload after completion or refusal, a
        // frame of a video never completed): garbage once the links have expired.
        var leftovers = 0;
        var linksExpired = now - TimeSpan.FromMinutes(o.UploadUrlMinutes) - LinkMargin;
        foreach (var prefix in MediaService.UploadPrefixes)
        {
            IReadOnlyList<string> keys;
            do
            {
                keys = await storage.ListOlderAsync(prefix, linksExpired, BatchSize, ct);
                await storage.DeleteAsync(keys, ct);
                leftovers += keys.Count;
            }
            while (keys.Count == BatchSize);
        }

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

        if (stale + unused + expired + leftovers > 0)
            logger.LogInformation(
                "Media cleanup: {Stale} stale uploads, {Unused} unused files removed, {Expired} originals expired, {Leftovers} upload leftovers",
                stale, unused, expired, leftovers);
        return new MediaCleanupResult(stale, unused, expired, leftovers);
    }

    private static IEnumerable<string> Keys(Attachment a) =>
        a.ThumbnailKey is null ? [a.StorageKey] : [a.StorageKey, a.ThumbnailKey];

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
