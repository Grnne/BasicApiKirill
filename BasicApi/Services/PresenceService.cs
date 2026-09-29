using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Users;
using BasicApi.Services.Events;

namespace BasicApi.Services;

/// <summary>
/// Онлайн и «печатает»: состояние (в памяти, <see cref="IUserStatusService"/>)
/// и рассылка изменений. Кому статус виден — решает <see cref="IChatPolicy"/>.
/// </summary>
public interface IPresenceService
{
    /// <summary>Новое соединение; первое соединение пользователя — «в сети» его контактам.</summary>
    Task ConnectedAsync(Guid userId, string connectionId, CancellationToken ct = default);

    /// <summary>
    /// Соединение закрыто; последнее — «не в сети» контактам и сброс «печатает».
    /// Без токена отмены: соединение к этому моменту уже оборвано.
    /// </summary>
    Task DisconnectedAsync(Guid userId, string connectionId);

    Task<ConnectionInfo> GetConnectionInfoAsync(Guid userId, string connectionId);

    /// <summary>«Печатает» в чате; 403 <c>NOT_A_MEMBER</c> для чужого чата.</summary>
    Task SetTypingAsync(Guid chatId, Guid userId, bool isTyping, CancellationToken ct = default);

    /// <summary>
    /// Двое только что стали собеседниками. При подключении «в сети» рассылается тем,
    /// с кем уже есть общий чат, так что без этого оба видели бы друг друга
    /// «не в сети» до переподключения. Сообщаем только «в сети»: «не в сети» клиент
    /// и так считает по умолчанию.
    /// </summary>
    Task IntroduceAsync(Guid userA, Guid userB, CancellationToken ct = default);

    /// <summary>Кто из контактов сейчас в сети (офлайн не перечисляются).</summary>
    Task<UserStatusResponseDto> GetContactsOnlineAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Статус одного пользователя; вне круга общих чатов — 404, чтобы не выдавать существование аккаунта.</summary>
    Task<UserStatusDto> GetUserStatusAsync(Guid viewerId, Guid targetId, CancellationToken ct = default);

    /// <summary>Статусы списка пользователей; не видимые вызывающему молча отбрасываются.</summary>
    Task<UserStatusResponseDto> GetUsersStatusAsync(
        Guid viewerId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);

    /// <summary>Кто печатает в чатах пользователя.</summary>
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

        // Debug, а не Information: на каждое подключение строка в проде не нужна.
        // IP и User-Agent не пишем — минимизация данных; они есть в sessions.
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

        // Ушёл последним соединением посреди набора — «печатает» гасим сразу, не дожидаясь TTL.
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
        // Спрашиваем только про чаты пользователя — чужие даже не читаются.
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
