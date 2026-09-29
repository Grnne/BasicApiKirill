using BasicApi.Storage.Dto;

namespace BasicApi.Storage.Interfaces;

/// <summary>Groups: their members with roles, settings and the action log.</summary>
public interface IGroupRepository
{
    /// <summary>A group with its creator as the owner and the others as members.</summary>
    Task CreateAsync(
        Guid chatId, string title, Guid creatorId, IReadOnlyCollection<Guid> memberIds, DateTime now,
        CancellationToken ct = default);

    /// <summary>All members, owner and admins first, then by joining time.</summary>
    Task<IReadOnlyList<ChatMember>> GetMembersAsync(Guid chatId, CancellationToken ct = default);

    /// <summary>
    /// Locks the chat row until the transaction ends — membership changes of one group go one at a
    /// time — and returns the number of members; null — no such chat.
    /// </summary>
    Task<int?> LockAsync(Guid chatId, CancellationToken ct = default);

    /// <summary>
    /// Adds members; the ones already in are skipped. They start with everything sent so far read.
    /// Returns who was added.
    /// </summary>
    Task<IReadOnlyList<Guid>> AddMembersAsync(
        Guid chatId, IReadOnlyCollection<Guid> userIds, DateTime now, CancellationToken ct = default);

    /// <summary>Removes the member; false — was not one.</summary>
    Task<bool> RemoveMemberAsync(Guid chatId, Guid userId, CancellationToken ct = default);

    Task SetRoleAsync(Guid chatId, Guid userId, string role, CancellationToken ct = default);

    /// <summary>Replaces the member's permission overrides; null clears them.</summary>
    Task SetPermissionsAsync(Guid chatId, Guid userId, string? permissionsJson, CancellationToken ct = default);

    /// <summary>Sets the title and settings of the group.</summary>
    Task UpdateAsync(Guid chatId, string title, string? settingsJson, DateTime now, CancellationToken ct = default);

    /// <summary>Deletes the group with its messages and members.</summary>
    Task DeleteAsync(Guid chatId, CancellationToken ct = default);

    Task AppendAuditAsync(ChatAuditEntry entry, CancellationToken ct = default);

    /// <summary>Newest first, strictly before <paramref name="beforeId"/> when given.</summary>
    Task<IReadOnlyList<ChatAuditEntry>> GetAuditAsync(Guid chatId, long? beforeId, int limit, CancellationToken ct = default);
}
