using BasicApi.Storage.Entities;

namespace BasicApi.Storage.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByUsernameOrEmailAsync(string usernameOrEmail, CancellationToken ct = default);
    Task<Guid> CreateAsync(User user, CancellationToken ct = default);

    Task<Guid?> GetIdByUsernameOrEmailAsync(string usernameOrEmail, CancellationToken ct = default);

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