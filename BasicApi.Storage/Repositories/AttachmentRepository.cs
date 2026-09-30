using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Storage.Repositories;

public sealed class AttachmentRepository(IDbSession db) : IAttachmentRepository
{
    public const string Columns = @"
        a.id AS Id, a.owner_id AS OwnerId, a.kind AS Kind, a.file_name AS FileName, a.mime AS Mime,
        a.size AS Size, a.sha256 AS Sha256, a.width AS Width, a.height AS Height,
        a.duration_ms AS DurationMs, a.waveform AS Waveform, a.storage_key AS StorageKey,
        a.thumbnail_key AS ThumbnailKey, a.storage_state AS StorageState, a.created_at AS CreatedAt,
        a.stored_at AS StoredAt";

    /// <summary>Who may download a file: for now only whoever uploaded it.</summary>
    private const string AccessibleTo = "a.owner_id = @userId";

    public Task CreateAsync(Attachment attachment, CancellationToken ct = default) =>
        db.ExecuteAsync(@"
            INSERT INTO attachments (id, owner_id, kind, file_name, mime, size, width, height, duration_ms,
                                     waveform, storage_key, storage_state, created_at)
            VALUES (@Id, @OwnerId, @Kind, @FileName, @Mime, @Size, @Width, @Height, @DurationMs,
                    @Waveform, @StorageKey, @StorageState, @CreatedAt)",
            attachment, ct);

    public Task<Attachment?> GetOwnAsync(Guid ownerId, Guid attachmentId, CancellationToken ct = default) =>
        db.QueryFirstOrDefaultAsync<Attachment>(
            $"SELECT {Columns} FROM attachments a WHERE a.id = @attachmentId AND a.owner_id = @ownerId",
            new { ownerId, attachmentId }, ct);

    public async Task<int> CountPendingAsync(Guid ownerId, CancellationToken ct = default) =>
        await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM attachments WHERE owner_id = @ownerId AND storage_state = 'pending'",
            new { ownerId }, ct);

    public async Task<bool> MarkStoredAsync(Attachment f, CancellationToken ct = default) =>
        await db.ExecuteAsync(@"
            UPDATE attachments
            SET storage_state = 'stored', stored_at = @StoredAt, size = @Size, sha256 = @Sha256, mime = @Mime,
                width = @Width, height = @Height, thumbnail_key = @ThumbnailKey
            WHERE id = @Id AND storage_state = 'pending'",
            f, ct) > 0;

    public Task<IReadOnlyList<Attachment>> GetAccessibleAsync(
        Guid userId, IReadOnlyCollection<Guid> attachmentIds, CancellationToken ct = default) =>
        db.QueryAsync<Attachment>($@"
            SELECT {Columns} FROM attachments a
            WHERE a.id = ANY(@ids) AND a.storage_state <> 'pending' AND ({AccessibleTo})",
            new { userId, ids = attachmentIds.Distinct().ToArray() }, ct);

    public Task<IReadOnlyList<Attachment>> GetStalePendingAsync(
        DateTime startedBefore, int limit, CancellationToken ct = default) =>
        db.QueryAsync<Attachment>($@"
            SELECT {Columns} FROM attachments a
            WHERE a.storage_state = 'pending' AND a.created_at < @startedBefore
            ORDER BY a.created_at
            LIMIT @limit",
            new { startedBefore, limit }, ct);

    public Task<int> DeleteAsync(IReadOnlyCollection<Guid> attachmentIds, CancellationToken ct = default) =>
        db.ExecuteAsync("DELETE FROM attachments WHERE id = ANY(@ids)", new { ids = attachmentIds.ToArray() }, ct);
}
