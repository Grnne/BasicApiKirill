using BasicApi.Storage.Entities;

namespace BasicApi.Storage.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByUsernameOrEmailAsync(string usernameOrEmail, CancellationToken ct = default);
    /// <summary>
    /// Inserts a user. Throws <see cref="Exceptions.DuplicateKeyException"/> when the
    /// username or email is already taken (unique constraint), so a registration race
    /// surfaces as 409 rather than 500.
    /// </summary>
    Task<Guid> CreateAsync(User user, CancellationToken ct = default);

    /// <summary>Records a successful sign-in.</summary>
    Task UpdateLastLoginAsync(Guid userId, DateTime lastLoginAt, CancellationToken ct = default);

    /// <summary>
    /// Id of an active user by username (case-insensitive). Deliberately not by email:
    /// an email must not reveal whether its owner has an account.
    /// </summary>
    Task<Guid?> GetIdByUsernameAsync(string username, CancellationToken ct = default);

    /// <summary>
    /// Returns a user by id, or null if no such user exists.
    /// </summary>
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Records when the user was last seen online.</summary>
    Task SetLastSeenAsync(Guid userId, DateTime at, CancellationToken ct = default);

    /// <summary>Sets or clears the avatar; false when it was already so.</summary>
    Task<bool> SetAvatarAsync(Guid userId, Guid? attachmentId, CancellationToken ct = default);

    /// <summary>The users with these ids; missing ones are simply absent.</summary>
    Task<IReadOnlyList<User>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    /// <summary>
    /// Searches users by display name or username using ILIKE (case-insensitive).
    /// Excludes the current user from results.
    /// Returns users ordered by display_name then username.
    /// </summary>
    Task<IEnumerable<User>> SearchByDisplayNameOrUsernameAsync(
        string query, Guid excludeUserId, int limit, CancellationToken ct = default);

    /// <summary>
    /// Returns total count of users matching the search query (excluding current user).
    /// </summary>
    Task<int> CountBySearchQueryAsync(string query, Guid excludeUserId, CancellationToken ct = default);
}