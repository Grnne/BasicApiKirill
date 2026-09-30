using BasicApi.Storage.Dto;
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

    /// <summary>
    /// Who may download a file: whoever uploaded it, and the members of every chat it was sent
    /// to. Deleting a message for everyone unlinks its files, so that access goes with it.
    /// A user's avatar is seen by everyone signed in except whom the user blocked, a group's — by
    /// its members.
    /// </summary>
    private const string AccessibleTo = @"
        a.owner_id = @userId
        OR EXISTS (
            SELECT 1 FROM message_attachments ma
            JOIN chat_members cm ON cm.chat_id = ma.chat_id AND cm.user_id = @userId
            WHERE ma.attachment_id = a.id)
        OR EXISTS (
            SELECT 1 FROM users u WHERE u.avatar_attachment_id = a.id
            AND NOT EXISTS (SELECT 1 FROM user_blocks b WHERE b.blocker_id = u.id AND b.blocked_id = @userId))
        OR EXISTS (
            SELECT 1 FROM chats c
            JOIN chat_members cm ON cm.chat_id = c.id AND cm.user_id = @userId
            WHERE c.avatar_attachment_id = a.id)";

    /// <summary>
    /// The files of a message as a JSON array, in album order, for queries that return
    /// messages: one subquery by the primary key instead of a second round trip.
    /// Append the message id column and <c>)::text</c>.
    /// </summary>
    public const string AttachmentsJsonOf = @"
        (SELECT json_agg(json_build_object(
                    'id', a.id, 'kind', a.kind, 'fileName', a.file_name, 'mimeType', a.mime, 'size', a.size,
                    'width', a.width, 'height', a.height, 'durationMs', a.duration_ms,
                    'waveform', encode(a.waveform, 'base64'), 'hasThumbnail', a.thumbnail_key IS NOT NULL,
                    'state', a.storage_state) ORDER BY ma.position)
         FROM message_attachments ma JOIN attachments a ON a.id = ma.attachment_id
         WHERE ma.message_id = ";

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

    public Task<int> LinkToMessageAsync(
        Guid messageId, Guid chatId, long seq, IReadOnlyList<AttachmentRef> files, CancellationToken ct = default) =>
        // Joined with the files: one removed by the cleanup a moment ago is skipped, not a foreign
        // key error — the caller sees the count fall short.
        db.ExecuteAsync(@"
            INSERT INTO message_attachments (message_id, position, attachment_id, chat_id, seq, kind)
            SELECT @messageId, f.position - 1, f.id, @chatId, @seq, f.kind
            FROM unnest(@ids, @kinds) WITH ORDINALITY AS f(id, kind, position)
            JOIN attachments a ON a.id = f.id",
            new
            {
                messageId, chatId, seq,
                ids = files.Select(f => f.Id).ToArray(),
                kinds = files.Select(f => f.Kind).ToArray()
            }, ct);

    public Task<IReadOnlyList<Attachment>> GetStalePendingAsync(
        DateTime startedBefore, int limit, CancellationToken ct = default) =>
        db.QueryAsync<Attachment>($@"
            SELECT {Columns} FROM attachments a
            WHERE a.storage_state = 'pending' AND a.created_at < @startedBefore
            ORDER BY a.created_at
            LIMIT @limit",
            new { startedBefore, limit }, ct);

    /// <summary>Nothing points to the file: no message, no user or group avatar.</summary>
    private const string Unreferenced = @"
        NOT EXISTS (SELECT 1 FROM message_attachments ma WHERE ma.attachment_id = a.id)
        AND NOT EXISTS (SELECT 1 FROM users u WHERE u.avatar_attachment_id = a.id)
        AND NOT EXISTS (SELECT 1 FROM chats c WHERE c.avatar_attachment_id = a.id)";

    public Task<IReadOnlyList<Attachment>> DeleteUnreferencedAsync(
        DateTime storedBefore, int limit, CancellationToken ct = default) =>
        // One statement: a file sent again while the cleanup looks is kept — the row it points to
        // is locked by the reference, and the check sees it.
        db.QueryAsync<Attachment>($@"
            DELETE FROM attachments
            WHERE id IN (
                SELECT a.id FROM attachments a
                WHERE a.storage_state <> 'pending' AND a.stored_at < @storedBefore AND {Unreferenced}
                ORDER BY a.stored_at
                LIMIT @limit
                FOR UPDATE SKIP LOCKED)
            RETURNING id AS Id, storage_key AS StorageKey, thumbnail_key AS ThumbnailKey, storage_state AS StorageState",
            new { storedBefore, limit }, ct);

    public Task<IReadOnlyList<Attachment>> GetExpiringAsync(DateTime storedBefore, int limit, CancellationToken ct = default) =>
        // Avatars are kept whole: they are shown all the time, and their preview is all they need anyway.
        db.QueryAsync<Attachment>($@"
            SELECT {Columns} FROM attachments a
            WHERE a.storage_state = 'stored' AND a.stored_at < @storedBefore
              AND NOT EXISTS (SELECT 1 FROM users u WHERE u.avatar_attachment_id = a.id)
              AND NOT EXISTS (SELECT 1 FROM chats c WHERE c.avatar_attachment_id = a.id)
            ORDER BY a.stored_at
            LIMIT @limit",
            new { storedBefore, limit }, ct);

    public Task<int> MarkExpiredAsync(IReadOnlyCollection<Guid> attachmentIds, CancellationToken ct = default) =>
        db.ExecuteAsync(
            "UPDATE attachments SET storage_state = 'expired' WHERE id = ANY(@ids) AND storage_state = 'stored'",
            new { ids = attachmentIds.ToArray() }, ct);

    public Task<int> DeleteAsync(IReadOnlyCollection<Guid> attachmentIds, CancellationToken ct = default) =>
        db.ExecuteAsync("DELETE FROM attachments WHERE id = ANY(@ids)", new { ids = attachmentIds.ToArray() }, ct);
}
