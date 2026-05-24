using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Users;
using BasicApi.Services;
using BasicApi.Storage.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BasicApi.Features.Users;

public class UsersHandler(
    IUserRepository userRepository,
    IChatRepository chatRepository,
    IUserStatusService userStatusService)
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

    /// <summary>
    /// Returns online status of all chat members for the current user.
    /// Only includes users who are currently online.
    /// </summary>
    public async Task<IActionResult> GetOnlineStatusAsync(Guid userId)
    {
        var memberIds = await chatRepository.GetAllChatMembersAsync(userId);

        if (memberIds.Count == 0)
            return new OkObjectResult(new UserStatusResponseDto());

        var memberSet = memberIds.ToHashSet();
        var onlineIds = await userStatusService.GetOnlineUserIdsAsync(memberSet);

        var items = onlineIds.Select(id => new UserStatusDto
        {
            UserId = id,
            IsOnline = true
        }).ToList();

        return new OkObjectResult(new UserStatusResponseDto { Items = items });
    }

    /// <summary>
    /// Returns who is currently typing in which chat, for all chats the current user is a member of.
    /// Only includes chats where the current user is a participant (privacy filter).
    /// </summary>
    public async Task<IActionResult> GetTypingStatusAsync(Guid userId)
    {
        var typingMap = await userStatusService.GetTypingStatusAsync(userId);

        if (typingMap.Count == 0)
            return new OkObjectResult(new TypingStatusResponseDto());

        // Filter by user's chats to avoid leaking info about chats the user isn't in
        var userChats = await chatRepository.GetUserChatsAsync(userId);
        var userChatIds = userChats.Select(c => c.Id).ToHashSet();

        var items = typingMap
            .Where(kvp => userChatIds.Contains(kvp.Key))
            .SelectMany(kvp => kvp.Value.Select(uid => new TypingStatusDto
            {
                UserId = uid,
                ChatId = kvp.Key,
                IsTyping = true
            }))
            .ToList();

        return new OkObjectResult(new TypingStatusResponseDto { Items = items });
    }
}
