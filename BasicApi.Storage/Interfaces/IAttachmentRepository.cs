using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;

namespace BasicApi.Storage.Interfaces;

public interface IAttachmentRepository
{
    Task CreateAsync(Attachment attachment, CancellationToken ct = default);

    /// <summary>The user's own file, in any state; null when there is none or it is someone else's.</summary>
    Task<Attachment?> GetOwnAsync(Guid ownerId, Guid attachmentId, CancellationToken ct = default);

    /// <summary>How many of the user's uploads are not finished yet.</summary>
    Task<int> CountPendingAsync(Guid ownerId, CancellationToken ct = default);

    /// <summary>
    /// Records what the server found in the uploaded object and makes the file usable; false when
    /// it is no longer pending (a concurrent completion won, or the upload was swept).
    /// </summary>
    Task<bool> MarkStoredAsync(Attachment checkedFile, CancellationToken ct = default);

    /// <summary>
    /// Of the given uploaded files, those the user may use and download: their own and the ones
    /// sent to chats they are in.
    /// </summary>
    Task<IReadOnlyList<Attachment>> GetAccessibleAsync(
        Guid userId, IReadOnlyCollection<Guid> attachmentIds, CancellationToken ct = default);

    /// <summary>Puts the files into the message, in this order; returns how many were found and put.</summary>
    Task<int> LinkToMessageAsync(Guid messageId, Guid chatId, long seq, IReadOnlyList<AttachmentRef> files, CancellationToken ct = default);

    /// <summary>Uploads started before the moment and never finished, oldest first.</summary>
    Task<IReadOnlyList<Attachment>> GetStalePendingAsync(DateTime startedBefore, int limit, CancellationToken ct = default);

    /// <summary>
    /// Deletes up to <paramref name="limit"/> uploaded files stored before the moment that nothing
    /// points to (no message, no avatar) and returns them; the objects are the caller's to remove.
    /// </summary>
    Task<IReadOnlyList<Attachment>> DeleteUnreferencedAsync(DateTime storedBefore, int limit, CancellationToken ct = default);

    /// <summary>Stored files older than the moment whose originals the retention policy removes; avatars are not.</summary>
    Task<IReadOnlyList<Attachment>> GetExpiringAsync(DateTime storedBefore, int limit, CancellationToken ct = default);

    /// <summary>Marks the files as having only their preview left.</summary>
    Task<int> MarkExpiredAsync(IReadOnlyCollection<Guid> attachmentIds, CancellationToken ct = default);

    /// <summary>Deletes the rows; the objects are the caller's to remove.</summary>
    Task<int> DeleteAsync(IReadOnlyCollection<Guid> attachmentIds, CancellationToken ct = default);
}
