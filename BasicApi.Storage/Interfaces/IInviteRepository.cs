namespace BasicApi.Storage.Interfaces;

public interface IInviteRepository
{
    Task CreateAsync(string codeHash, Guid createdBy, DateTime createdAt, DateTime expiresAt, CancellationToken ct = default);

    /// <summary>The user's invitations neither used nor expired yet.</summary>
    Task<int> CountOpenAsync(Guid createdBy, DateTime now, CancellationToken ct = default);

    /// <summary>Whether the invitation exists, is unused and has not expired.</summary>
    Task<bool> IsOpenAsync(string codeHash, DateTime now, CancellationToken ct = default);

    /// <summary>Uses the invitation up for the new user; false when it is not open (any more).</summary>
    Task<bool> UseAsync(string codeHash, Guid userId, DateTime now, CancellationToken ct = default);
}
