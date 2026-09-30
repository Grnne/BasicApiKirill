using BasicApi.Storage.Dto;

namespace BasicApi.Storage.Interfaces;

/// <summary>
/// A user's update journal: each user has their own gapless pts numbering
/// (the number is issued in the same transaction as the write; a rollback leaves no holes).
/// </summary>
public interface IUpdateJournal
{
    /// <summary>Adds the same update to the journals of several users.</summary>
    Task AppendAsync(IReadOnlyCollection<Guid> userIds, string type, string payloadJson, CancellationToken ct = default);

    /// <summary>The user's last pts; 0 - the journal is empty.</summary>
    Task<long> GetPtsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Updates with pts greater than <paramref name="sincePts"/>, in order.</summary>
    Task<IReadOnlyList<UserUpdate>> GetSinceAsync(Guid userId, long sincePts, int limit, CancellationToken ct = default);

    /// <summary>Records that the device has received the journal up to <paramref name="pts"/>; never moves backwards.</summary>
    Task AckAsync(Guid userId, Guid sessionFamilyId, long pts, CancellationToken ct = default);

    /// <summary>
    /// Moves the user's delivery pointers to the messages their journal carried up to
    /// <paramref name="pts"/> beyond what any of their devices acknowledged before. Call it before
    /// <see cref="AckAsync"/> in the same transaction. Returns the pointers that moved.
    /// </summary>
    Task<IReadOnlyList<PointerMove>> DeliverUpToAsync(Guid userId, long pts, CancellationToken ct = default);

    /// <summary>
    /// Deletes journal entries older than <paramref name="olderThan"/> - no more than
    /// <paramref name="batchSize"/> at a time, to avoid holding long locks.
    /// </summary>
    Task<int> DeleteOlderThanAsync(DateTime olderThan, int batchSize, CancellationToken ct = default);
}
