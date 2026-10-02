using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Users;
using BasicApi.Services.Events;
using BasicApi.Storage.Interfaces;

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

    /// <summary>
    /// The same for a group: the new members learn who of the group is online, and the members
    /// already there learn who of the new ones is.
    /// </summary>
    Task IntroduceAsync(
        IReadOnlyCollection<Guid> newMembers, IReadOnlyCollection<Guid> existingMembers, CancellationToken ct = default);

    /// <summary>Which contacts are online now (offline ones are not listed).</summary>
    Task<UserStatusResponseDto> GetContactsOnlineAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The status of a single user; outside the circle of shared chats - 404, so as not to reveal that the account exists.</summary>
    Task<UserStatusDto> GetUserStatusAsync(Guid viewerId, Guid targetId, CancellationToken ct = default);

    /// <summary>Statuses for a list of users; those not visible to the caller are silently dropped.</summary>
    Task<UserStatusResponseDto> GetUsersStatusAsync(
        Guid viewerId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);

    /// <summary>Who is typing in the user's chats.</summary>
    Task<TypingStatusResponseDto> GetTypingAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// The user and <paramref name="lost"/> stopped seeing each other's presence (a privacy setting,
    /// a block), <paramref name="gained"/> started: whoever of them is online appears offline to the
    /// other, or online.
    /// </summary>
    Task PeersChangedAsync(
        Guid userId, IReadOnlyCollection<Guid> lost, IReadOnlyCollection<Guid> gained, CancellationToken ct = default);
}

public sealed record ConnectionInfo(int ConnectionCount, bool IsCurrentActive);

public sealed class PresenceService(
    IUserStatusService status,
    IMembershipService membership,
    IChatPolicy policy,
    IChatEventPublisher events,
    ILogger<PresenceService> logger,
    IUserRepository? users = null,
    TimeProvider? time = null) : IPresenceService
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

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

        if (!isFirstConnection)
            return;
        // Also when coming online: after a crash no disconnect records it, and last seen should not
        // be older than the last time the user was around.
        await RecordLastSeenAsync(userId);
        // The user is online already: the contacts are told even if the connection drops meanwhile
        // (then they are told "offline" right after).
        await events.UserOnlineChangedAsync(userId, true, await policy.GetPresenceAudienceAsync(userId, CancellationToken.None),
            CancellationToken.None);
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

        await RecordLastSeenAsync(userId);
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

    public Task IntroduceAsync(Guid userA, Guid userB, CancellationToken ct = default) =>
        IntroduceAsync([userA], [userB], ct);

    public async Task IntroduceAsync(
        IReadOnlyCollection<Guid> newMembers, IReadOnlyCollection<Guid> existingMembers, CancellationToken ct = default)
    {
        var everyone = newMembers.Concat(existingMembers).ToHashSet();
        var fresh = newMembers.ToHashSet();

        // One event per online member, not per pair: a group of hundreds stays cheap. Only to
        // those it may be shown to: the rule is mutual, so whom the member may see may see them.
        foreach (var userId in await status.GetOnlineUserIdsAsync(everyone))
        {
            var candidates = (fresh.Contains(userId) ? everyone : fresh).Where(id => id != userId).ToList();
            if (candidates.Count == 0)
                continue;
            var recipients = (await policy.FilterPresenceVisibleAsync(userId, candidates, ct)).ToList();
            if (recipients.Count > 0)
                await events.UserOnlineChangedAsync(userId, true, recipients, ct);
        }
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
        var known = await KnownAsync(viewerId, [targetId], ct);
        if (known.Count == 0)
            throw new NotFoundException("User not found", "USER_NOT_FOUND");

        return (await StatusesAsync(viewerId, known, ct))[0];
    }

    public async Task<UserStatusResponseDto> GetUsersStatusAsync(
        Guid viewerId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
    {
        if (userIds is null || userIds.Count == 0)
            throw new BadRequestException("userIds must not be empty", "INVALID_REQUEST");

        if (userIds.Count > UserStatusBatchRequestDto.MaxUserIds)
            throw new BadRequestException(
                $"At most {UserStatusBatchRequestDto.MaxUserIds} userIds per request", "TOO_MANY_IDS");

        var requested = await KnownAsync(viewerId, userIds, ct);
        return requested.Count == 0
            ? new UserStatusResponseDto()
            : new UserStatusResponseDto { Items = await StatusesAsync(viewerId, requested, ct) };
    }

    /// <summary>Whom the viewer may ask about at all: themselves and those who share a chat with them.</summary>
    private async Task<List<Guid>> KnownAsync(Guid viewerId, IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        var contacts = (await membership.GetContactIdsAsync(viewerId, ct)).ToHashSet();
        contacts.Add(viewerId);
        return [.. userIds.Distinct().Where(contacts.Contains)];
    }

    /// <summary>
    /// Statuses of known users: a contact who hides their presence from the viewer (or whose the
    /// viewer cannot see — privacy, block) is simply offline, with no last seen.
    /// </summary>
    private async Task<List<UserStatusDto>> StatusesAsync(Guid viewerId, List<Guid> known, CancellationToken ct)
    {
        var visible = await policy.FilterPresenceVisibleAsync(viewerId, known, ct);
        var onlineIds = await status.GetOnlineUserIdsAsync(visible);
        var items = known.Select(id => new UserStatusDto { UserId = id, IsOnline = onlineIds.Contains(id) }).ToList();
        await WithLastSeenAsync([.. items.Where(i => visible.Contains(i.UserId))], ct);
        return items;
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

    public async Task PeersChangedAsync(
        Guid userId, IReadOnlyCollection<Guid> lost, IReadOnlyCollection<Guid> gained, CancellationToken ct = default)
    {
        if (lost.Count + gained.Count == 0)
            return;
        var online = await status.GetOnlineUserIdsAsync(new HashSet<Guid>([userId, .. lost, .. gained]));

        if (online.Contains(userId))
        {
            if (lost.Count > 0)
                await events.UserOnlineChangedAsync(userId, false, lost, ct);
            if (gained.Count > 0)
                await events.UserOnlineChangedAsync(userId, true, gained, ct);
        }
        foreach (var peer in lost.Where(online.Contains))
            await events.UserOnlineChangedAsync(peer, false, [userId], ct);
        foreach (var peer in gained.Where(online.Contains))
            await events.UserOnlineChangedAsync(peer, true, [userId], ct);
    }

    private async Task RecordLastSeenAsync(Guid userId)
    {
        if (users is null)
            return;
        try
        {
            await users.SetLastSeenAsync(userId, _time.GetUtcNow().UtcDateTime);
        }
        catch (Exception ex)
        {
            // Presence must not fail because of it: last seen is a nicety.
            logger.LogWarning(ex, "Could not record last seen for {UserId}", userId);
        }
    }

    /// <summary>Last seen for the offline ones — the caller already sees their presence.</summary>
    private async Task<List<UserStatusDto>> WithLastSeenAsync(List<UserStatusDto> items, CancellationToken ct)
    {
        var offline = items.Where(i => !i.IsOnline).Select(i => i.UserId).ToList();
        if (users is null || offline.Count == 0)
            return items;
        var seen = (await users.GetByIdsAsync(offline, ct)).ToDictionary(u => u.Id, u => u.LastSeenAt);
        foreach (var item in items.Where(i => !i.IsOnline))
            item.LastSeenAt = seen.GetValueOrDefault(item.UserId);
        return items;
    }

    private static Guid[] Others(IReadOnlyList<Guid> memberIds, Guid userId) =>
        [.. memberIds.Where(id => id != userId)];
}
