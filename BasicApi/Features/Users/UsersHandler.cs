using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Users;
using BasicApi.Storage.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BasicApi.Features.Users;

public class UsersHandler(IUserRepository userRepository)
{
    public async Task<IActionResult> GetUserIdAsync(string usernameOrEmail)
    {
        var userId = await userRepository.GetIdByUsernameOrEmailAsync(usernameOrEmail);

        if (!userId.HasValue || userId == Guid.Empty)
            throw new NotFoundException("User not found", "USER_NOT_FOUND");

        return new OkObjectResult(new UserIdResponseDto { UserId = userId.Value });
    }

    /// <summary>
    /// Searches users by display name or username (Telegram-style ILIKE search).
    /// Excludes the current user from results.
    /// </summary>
    public async Task<IActionResult> SearchUsersAsync(Guid currentUserId, string query, int limit)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new BadRequestException("Query cannot be empty", "INVALID_QUERY");

        var usersTask = userRepository.SearchByDisplayNameOrUsernameAsync(query, currentUserId, limit);
        var countTask = userRepository.CountBySearchQueryAsync(query, currentUserId);

        await Task.WhenAll(usersTask, countTask);

        var items = usersTask.Result.Select(u => new UserSearchResultDto
        {
            UserId = u.Id,
            Username = u.Username,
            DisplayName = u.DisplayName
        }).ToList();

        return new OkObjectResult(new SearchUsersResponseDto
        {
            Items = items,
            Query = query,
            TotalCount = countTask.Result
        });
    }
}