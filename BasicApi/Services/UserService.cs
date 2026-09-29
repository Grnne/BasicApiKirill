using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Users;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Services;

public interface IUserService
{
    /// <summary>Id активного пользователя по логину (без учёта регистра); 404 <c>USER_NOT_FOUND</c>.</summary>
    Task<UserIdResponseDto> GetUserIdAsync(string username, CancellationToken ct = default);

    /// <summary>
    /// Профиль самого вызывающего, с почтой: клиент, восстановивший сессию по сохранённому
    /// токену, получает те же данные, что вернули бы вход или регистрация.
    /// </summary>
    Task<OwnProfileResponseDto> GetOwnProfileAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Публичный профиль. Почты нет — она видна только владельцу. Онлайн-статуса тоже нет:
    /// он доступен только собеседникам, через <see cref="IPresenceService"/>.
    /// </summary>
    Task<UserProfileResponseDto> GetUserProfileAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Поиск по имени или логину (ILIKE), без самого вызывающего.</summary>
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
        // Токен жив, а аккаунта нет — клиенту пора выбросить токен и войти заново.
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
