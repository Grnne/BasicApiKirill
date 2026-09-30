using BasicApi.Storage.Entities;

namespace BasicApi.Storage.Interfaces;

public interface IPrivacyRepository
{
    /// <summary>The user's settings; the defaults (everybody) when never changed.</summary>
    Task<UserPrivacy> GetAsync(Guid userId, CancellationToken ct = default);

    Task SaveAsync(UserPrivacy privacy, CancellationToken ct = default);

    /// <summary>
    /// Who sees the user's online and last seen, and whose the user sees — the relation is mutual:
    /// contacts (a shared chat), neither of the two hides it (<c>nobody</c>), no block either way.
    /// With <paramref name="among"/> — only of those.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetPresencePeersAsync(
        Guid userId, IReadOnlyCollection<Guid>? among = null, CancellationToken ct = default);

    /// <summary>The two users are in some chat together.</summary>
    Task<bool> ShareChatAsync(Guid userId, Guid otherId, CancellationToken ct = default);

    /// <summary>Whether <paramref name="blockerId"/> blocked <paramref name="blockedId"/>.</summary>
    Task<bool> IsBlockedAsync(Guid blockerId, Guid blockedId, CancellationToken ct = default);

    /// <summary>Of <paramref name="userIds"/>, those who blocked <paramref name="userId"/>.</summary>
    Task<IReadOnlySet<Guid>> GetBlockersAsync(Guid userId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);

    /// <summary>Adds the block; false when it was there.</summary>
    Task<bool> BlockAsync(Guid blockerId, Guid blockedId, DateTime now, CancellationToken ct = default);

    /// <summary>Removes the block; false when there was none.</summary>
    Task<bool> UnblockAsync(Guid blockerId, Guid blockedId, CancellationToken ct = default);

    /// <summary>Whom the user blocked, the latest first.</summary>
    Task<IReadOnlyList<User>> GetBlockedAsync(Guid blockerId, CancellationToken ct = default);

    /// <summary>
    /// Of <paramref name="candidateIds"/>, whom <paramref name="adderId"/> may not add to a group:
    /// they allow nobody, or contacts only and share no chat with the adder, or blocked the adder.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetGroupAddRefusalsAsync(
        Guid adderId, IReadOnlyCollection<Guid> candidateIds, CancellationToken ct = default);
}
