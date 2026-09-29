using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Users;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Services;

public interface IUserService
{
    /// <summary>Id of an active user by login (case-insensitive); 404 <c>USER_NOT_FOUND</c>.</summary>
    Task<UserIdResponseDto> GetUserIdAsync(string username, CancellationToken ct = default);

    /// <summary>
    /// The caller's own profile, with email: a client that restored a session from a saved
    /// token gets the same data that login or registration would return.
    /// </summary>
    Task<OwnProfileResponseDto> GetOwnProfileAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Public profile. No email — it is visible only to the owner. No online status either:
    /// it is available only to counterparts, via <see cref="IPresenceService"/>.
    /// </summary>
    Task<UserProfileResponseDto> GetUserProfileAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Search by name or login (ILIKE), excluding the caller.</summary>
    Task<SearchUsersResponseDto> SearchUsersAsync(
        Guid currentUserId, string query, int limit, CancellationToken ct = default);
}

public sealed class UserService(IUserRepository userRepository) : IUserService
{
    public async Task<UserIdResponseDto> GetUserIdAsync(string username, CancellationToken ct = default)
    {
        var userId = await userRepository.GetIdByUsernameAsync(username, ct)
            ?? throw UserNotFound();

        return new UserIdResponseDto { UserId = userId };
    }

    public async Task<OwnProfileResponseDto> GetOwnProfileAsync(Guid userId, CancellationToken ct = default)
    {
        // The token is alive but the account is gone — the client should discard the token and sign in again.
        var user = await userRepository.GetByIdAsync(userId, ct)
            ?? throw UserNotFound();

        return new OwnProfileResponseDto
        {
            UserId = user.Id,
            Username = user.Username,
            Email = user.Email,
            DisplayName = user.DisplayName
        };
    }

    public async Task<UserProfileResponseDto> GetUserProfileAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await userRepository.GetByIdAsync(userId, ct)
            ?? throw UserNotFound();

        return new UserProfileResponseDto
        {
            UserId = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName
        };
    }

    public async Task<SearchUsersResponseDto> SearchUsersAsync(
        Guid currentUserId, string query, int limit, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new BadRequestException("Query cannot be empty", "INVALID_QUERY");

        var usersTask = userRepository.SearchByDisplayNameOrUsernameAsync(query, currentUserId, limit, ct);
        var countTask = userRepository.CountBySearchQueryAsync(query, currentUserId, ct);

        await Task.WhenAll(usersTask, countTask);

        return new SearchUsersResponseDto
        {
            Items = [.. usersTask.Result.Select(u => new UserSearchResultDto
            {
                UserId = u.Id,
                Username = u.Username,
                DisplayName = u.DisplayName
            })],
            Query = query,
            TotalCount = countTask.Result
        };
    }

    private static NotFoundException UserNotFound() => new("User not found", "USER_NOT_FOUND");
}
