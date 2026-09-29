using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Users;
using BasicApi.Services.Events;

namespace BasicApi.Services;

/// <summary>
/// Online and "typing": the state (in memory, <see cref="IUserStatusService"/>)
/// and broadcasting of changes. Who can see a status is decided by <see cref="IChatPolicy"/>.
/// </summary>
public interface IPresenceService
{
    /// <summary>A new connection; a user's first connection means "online" to their contacts.</summary>
    Task ConnectedAsync(Guid userId, string connectionId, CancellationToken ct = default);

    /// <summary>
    /// Connection closed; the last one means "offline" to contacts and resets "typing".
    /// No cancellation token: by this point the connection has already dropped.
    /// </summary>
    Task DisconnectedAsync(Guid userId, string connectionId);

    Task<ConnectionInfo> GetConnectionInfoAsync(Guid userId, string connectionId);

    /// <summary>"Typing" in a chat; 403 <c>NOT_A_MEMBER</c> for a chat one is not in.</summary>
    Task SetTypingAsync(Guid chatId, Guid userId, bool isTyping, CancellationToken ct = default);

    /// <summary>
    /// Two users have just become companions. On connect, "online" is broadcast to those
    /// who already share a chat, so without this the two would see each other as
    /// "offline" until they reconnect. Only "online" is announced: the client
    /// assumes "offline" by default anyway.
    /// </summary>
    Task IntroduceAsync(Guid userA, Guid userB, CancellationToken ct = default);

    /// <summary>Which contacts are online now (offline ones are not listed).</summary>
    Task<UserStatusResponseDto> GetContactsOnlineAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The status of a single user; outside the circle of shared chats - 404, so as not to reveal that the account exists.</summary>
    Task<UserStatusDto> GetUserStatusAsync(Guid viewerId, Guid targetId, CancellationToken ct = default);

    /// <summary>Statuses for a list of users; those not visible to the caller are silently dropped.</summary>
    Task<UserStatusResponseDto> GetUsersStatusAsync(
        Guid viewerId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);

    /// <summary>Who is typing in the user's chats.</summary>
    Task<TypingStatusResponseDto> GetTypingAsync(Guid userId, CancellationToken ct = default);
}

public sealed record ConnectionInfo(int ConnectionCount, bool IsCurrentActive);

public sealed class PresenceService(
    IUserStatusService status,
    IMembershipService membership,
    IChatPolicy policy,
    IChatEventPublisher events,
    ILogger<PresenceService> logger) : IPresenceService
{
    public async Task ConnectedAsync(Guid userId, string connectionId, CancellationToken ct = default)
    {
        var isFirstConnection = await status.SetUserOnlineStatusAsync(userId, connectionId, true);

        // Debug, not Information: a line per connection is not needed in prod.
        // We do not log IP and User-Agent - data minimisation; they are in sessions.
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Connected: userId={UserId}, connectionId={ConnectionId}, connections={Count}",
                userId, connectionId, await status.GetConnectionCountAsync(userId));
        }

        if (isFirstConnection)
            await events.UserOnlineChangedAsync(userId, true, await policy.GetPresenceAudienceAsync(userId, ct), ct);
    }

    public async Task DisconnectedAsync(Guid userId, string connectionId)
    {
        var wentOffline = await status.SetUserOnlineStatusAsync(userId, connectionId, false);

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Disconnected: userId={UserId}, connectionId={ConnectionId}, connections={Count}",
                userId, connectionId, await status.GetConnectionCountAsync(userId));
        }

        if (!wentOffline)
            return;

        await events.UserOnlineChangedAsync(userId, false, await policy.GetPresenceAudienceAsync(userId));

        // Left with the last connection in the middle of typing - clear "typing" right away, without waiting for the TTL.
        foreach (var chatId in await status.ClearTypingAsync(userId))
        {
            var memberIds = await membership.GetMemberIdsAsync(chatId);
            await events.TypingChangedAsync(chatId, userId, false, Others(memberIds, userId));
        }
    }

    public async Task<ConnectionInfo> GetConnectionInfoAsync(Guid userId, string connectionId) =>
        new(await status.GetConnectionCountAsync(userId), await status.IsConnectionActiveAsync(userId, connectionId));

    public async Task SetTypingAsync(Guid chatId, Guid userId, bool isTyping, CancellationToken ct = default)
    {
        await policy.DemandPostAsync(userId, chatId, ct);
        var memberIds = await membership.GetMemberIdsAsync(chatId, ct);

        await status.SetTypingAsync(chatId, userId, isTyping);
        await events.TypingChangedAsync(chatId, userId, isTyping, Others(memberIds, userId), ct);
    }

    public async Task IntroduceAsync(Guid userA, Guid userB, CancellationToken ct = default)
    {
        var online = await status.GetOnlineUserIdsAsync(new HashSet<Guid> { userA, userB });

        if (online.Contains(userA))
            await events.UserOnlineChangedAsync(userA, true, [userB], ct);
        if (online.Contains(userB))
            await events.UserOnlineChangedAsync(userB, true, [userA], ct);
    }

    public async Task<UserStatusResponseDto> GetContactsOnlineAsync(Guid userId, CancellationToken ct = default)
    {
        var contacts = await membership.GetContactIdsAsync(userId, ct);
        if (contacts.Count == 0)
            return new UserStatusResponseDto();

        var visible = await policy.FilterPresenceVisibleAsync(userId, contacts, ct);
        var onlineIds = await status.GetOnlineUserIdsAsync(visible);

        return new UserStatusResponseDto
        {
            Items = [.. onlineIds.Select(id => new UserStatusDto { UserId = id, IsOnline = true })]
        };
    }

    public async Task<UserStatusDto> GetUserStatusAsync(Guid viewerId, Guid targetId, CancellationToken ct = default)
    {
        if ((await policy.FilterPresenceVisibleAsync(viewerId, [targetId], ct)).Count == 0)
            throw new NotFoundException("User not found", "USER_NOT_FOUND");

        var onlineIds = await status.GetOnlineUserIdsAsync(new HashSet<Guid> { targetId });
        return new UserStatusDto { UserId = targetId, IsOnline = onlineIds.Contains(targetId) };
    }

    public async Task<UserStatusResponseDto> GetUsersStatusAsync(
        Guid viewerId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
    {
        if (userIds is null || userIds.Count == 0)
            throw new BadRequestException("userIds must not be empty", "INVALID_REQUEST");

        if (userIds.Count > UserStatusBatchRequestDto.MaxUserIds)
            throw new BadRequestException(
                $"At most {UserStatusBatchRequestDto.MaxUserIds} userIds per request", "TOO_MANY_IDS");

        var requested = await policy.FilterPresenceVisibleAsync(viewerId, userIds, ct);
        if (requested.Count == 0)
            return new UserStatusResponseDto();

        var onlineIds = await status.GetOnlineUserIdsAsync(requested);

        return new UserStatusResponseDto
        {
            Items = [.. requested.Select(id => new UserStatusDto { UserId = id, IsOnline = onlineIds.Contains(id) })]
        };
    }

    public async Task<TypingStatusResponseDto> GetTypingAsync(Guid userId, CancellationToken ct = default)
    {
        // Ask only about the user's chats - others' are not even read.
        var typingMap = await status.GetTypingStatusAsync(await membership.GetChatIdsAsync(userId, ct));

        return new TypingStatusResponseDto
        {
            Items = [.. typingMap.SelectMany(kvp => kvp.Value.Select(uid => new TypingStatusDto
            {
                UserId = uid,
                ChatId = kvp.Key,
                IsTyping = true
            }))]
        };
    }

    private static Guid[] Others(IReadOnlyList<Guid> memberIds, Guid userId) =>
        [.. memberIds.Where(id => id != userId)];
}
