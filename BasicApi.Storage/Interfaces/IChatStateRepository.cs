namespace BasicApi.Storage.Interfaces;

/// <summary>How a member keeps their chats: pinned, archived, muted (D9).</summary>
public interface IChatStateRepository
{
    /// <summary>Serializes changes to one user's pins: the limit holds under concurrent pins.</summary>
    Task LockUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The user's pinned chats, top first.</summary>
    Task<IReadOnlyList<Guid>> GetPinnedAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Sets the pinned chats in this order (1 — the top); the others are unpinned.</summary>
    Task SetPinnedAsync(Guid userId, IReadOnlyList<Guid> chatIds, CancellationToken ct = default);

    /// <summary>Archives or brings back; false when it already was so or the user is not a member.</summary>
    Task<bool> SetArchivedAsync(Guid chatId, Guid userId, DateTime? archivedAt, CancellationToken ct = default);

    /// <summary>Mutes until the moment, or unmutes (null); false when it already was so.</summary>
    Task<bool> SetMutedUntilAsync(Guid chatId, Guid userId, DateTime? mutedUntil, CancellationToken ct = default);

    /// <summary>
    /// A new message brings the chat back from the archive of members who have not muted it (D9);
    /// returns whose.
    /// </summary>
    Task<IReadOnlyList<Guid>> UnarchiveOnMessageAsync(Guid chatId, Guid senderId, DateTime now, CancellationToken ct = default);

    /// <summary>The member's state of the chat; null for a non-member.</summary>
    Task<ChatState?> GetAsync(Guid chatId, Guid userId, CancellationToken ct = default);
}

public sealed class ChatState
{
    public int? PinnedPosition { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public DateTime? MutedUntil { get; set; }
}
