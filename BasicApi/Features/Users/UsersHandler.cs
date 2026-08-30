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
    /// Returns the caller's own profile, including the email.
    /// Lets a client that restored a session from a stored token recover the same
    /// user data that login/register would have returned.
    /// </summary>
    public async Task<IActionResult> GetOwnProfileAsync(Guid userId)
    {
        var user = await userRepository.GetByIdAsync(userId);

        // Token is valid but the account is gone — the client should drop the token and re-login
        if (user is null)
            throw new NotFoundException("User not found", "USER_NOT_FOUND");

        return new OkObjectResult(new OwnProfileResponseDto
        {
            UserId = user.Id,
            Username = user.Username,
            Email = user.Email,
            DisplayName = user.DisplayName
        });
    }

    /// <summary>
    /// Returns the public profile of a user by id.
    /// Email is intentionally not exposed — it is private to the account owner.
    /// Online status is not exposed either; use /api/users/status, which is
    /// restricted to members of the caller's chats.
    /// </summary>
    public async Task<IActionResult> GetUserProfileAsync(Guid userId)
    {
        var user = await userRepository.GetByIdAsync(userId);

        if (user is null)
            throw new NotFoundException("User not found", "USER_NOT_FOUND");

        return new OkObjectResult(new UserProfileResponseDto
        {
            UserId = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName
        });
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
    /// Returns the online status of a single user.
    /// Visible only for users the caller shares a chat with (plus the caller themselves) —
    /// same privacy scope as <see cref="GetOnlineStatusAsync"/>. Anything outside that
    /// scope is reported as 404 so the endpoint cannot be used to probe account existence.
    /// </summary>
    public async Task<IActionResult> GetUserStatusAsync(Guid currentUserId, Guid targetUserId)
    {
        if (!await IsVisibleToAsync(currentUserId, targetUserId))
            throw new NotFoundException("User not found", "USER_NOT_FOUND");

        var onlineIds = await userStatusService.GetOnlineUserIdsAsync(new HashSet<Guid> { targetUserId });

        return new OkObjectResult(new UserStatusDto
        {
            UserId = targetUserId,
            IsOnline = onlineIds.Contains(targetUserId)
        });
    }

    /// <summary>
    /// Returns the online status of an explicit set of users.
    /// Unlike <see cref="GetOnlineStatusAsync"/>, offline users are reported
    /// explicitly with <c>isOnline: false</c> instead of being omitted.
    /// IDs the caller shares no chat with are silently dropped from the response.
    /// </summary>
    public async Task<IActionResult> GetUsersStatusAsync(Guid currentUserId, IReadOnlyCollection<Guid> userIds)
    {
        if (userIds is null || userIds.Count == 0)
            throw new BadRequestException("userIds must not be empty", "INVALID_REQUEST");

        if (userIds.Count > UserStatusBatchRequestDto.MaxUserIds)
            throw new BadRequestException(
                $"At most {UserStatusBatchRequestDto.MaxUserIds} userIds per request", "TOO_MANY_IDS");

        var memberIds = await chatRepository.GetAllChatMembersAsync(currentUserId);
        var visible = memberIds.ToHashSet();
        visible.Add(currentUserId);

        var requested = userIds.Where(visible.Contains).ToHashSet();

        if (requested.Count == 0)
            return new OkObjectResult(new UserStatusResponseDto());

        var onlineIds = await userStatusService.GetOnlineUserIdsAsync(requested);

        var items = requested
            .Select(id => new UserStatusDto { UserId = id, IsOnline = onlineIds.Contains(id) })
            .ToList();

        return new OkObjectResult(new UserStatusResponseDto { Items = items });
    }

    /// <summary>
    /// A user's presence is visible to the caller when they share at least one chat,
    /// or when the caller is asking about themselves.
    /// </summary>
    private async Task<bool> IsVisibleToAsync(Guid currentUserId, Guid targetUserId)
    {
        if (currentUserId == targetUserId)
            return true;

        var memberIds = await chatRepository.GetAllChatMembersAsync(currentUserId);
        return memberIds.Contains(targetUserId);
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
