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
    Task<UserProfileResponseDto> GetUserProfileAsync(Guid userId, CancellationToken ct = default, Guid? viewerId = null);

    /// <summary>Search by name or login (ILIKE), excluding the caller.</summary>
    Task<SearchUsersResponseDto> SearchUsersAsync(
        Guid currentUserId, string query, int limit, CancellationToken ct = default);
}

public sealed class UserService(IUserRepository userRepository, IPrivacyRepository? privacy = null) : IUserService
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

        return OwnProfile(user);
    }

    public async Task<UserProfileResponseDto> GetUserProfileAsync(Guid userId, CancellationToken ct = default, Guid? viewerId = null)
    {
        var user = await userRepository.GetByIdAsync(userId, ct)
            ?? throw UserNotFound();
        if (viewerId is { } viewer && await HiddenAvatarsAsync(viewer, [user.Id], ct) is { Count: > 0 })
            user.AvatarAttachmentId = null;

        return new UserProfileResponseDto
        {
            UserId = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            AvatarId = user.AvatarAttachmentId
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
        var hidden = await HiddenAvatarsAsync(currentUserId, [.. usersTask.Result.Select(u => u.Id)], ct);

        return new SearchUsersResponseDto
        {
            Items = [.. usersTask.Result.Select(u => new UserSearchResultDto
            {
                UserId = u.Id,
                Username = u.Username,
                DisplayName = u.DisplayName,
                AvatarId = hidden.Contains(u.Id) ? null : u.AvatarAttachmentId
            })],
            Query = query,
            TotalCount = countTask.Result
        };
    }

    /// <summary>Of these users, those who blocked the viewer: their avatars are not shown to them.</summary>
    private async Task<IReadOnlySet<Guid>> HiddenAvatarsAsync(Guid viewerId, IReadOnlyCollection<Guid> userIds, CancellationToken ct) =>
        privacy is null ? new HashSet<Guid>() : await privacy.GetBlockersAsync(viewerId, userIds, ct);

    public static OwnProfileResponseDto OwnProfile(Storage.Entities.User user) => new()
    {
        UserId = user.Id,
        Username = user.Username,
        Email = user.Email,
        DisplayName = user.DisplayName,
        AvatarId = user.AvatarAttachmentId
    };

    private static NotFoundException UserNotFound() => new("User not found", "USER_NOT_FOUND");
}
