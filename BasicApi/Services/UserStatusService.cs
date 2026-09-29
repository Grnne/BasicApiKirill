using System.Collections.Concurrent;

namespace BasicApi.Services;

/// <summary>
/// In-memory tracker for user online/offline and typing status.
/// Presence changes take a per-user lock, so connect/disconnect races cannot
/// lose a connection or report online/offline twice.
/// NOTE: For horizontal scaling, replace with Redis or SignalR Redis backplane.
/// </summary>
public class UserStatusService(TimeProvider? timeProvider = null) : IUserStatusService
{
    /// <summary>
    /// How long "typing" lives without a refresh. Clients repeat Typing(true) while the
    /// user types; if they vanish without Typing(false), the state expires on its own.
    /// </summary>
    public static readonly TimeSpan TypingTtl = TimeSpan.FromSeconds(6);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// Соединения одного пользователя. Все изменения — под lock на этом объекте
    /// (блокировка на пользователя: разные пользователи друг другу не мешают).
    /// </summary>
    private sealed class Connections
    {
        public readonly HashSet<string> Ids = [];

        /// <summary>
        /// Набор уже вынут из словаря (ушло последнее соединение). Подключение,
        /// успевшее взять ссылку на него, должно взять новый — иначе оно попало бы
        /// в «осиротевший» набор, и пользователь выглядел бы офлайн, будучи на связи.
        /// </summary>
        public bool Removed;
    }

    private readonly ConcurrentDictionary<Guid, Connections> _onlineUsers = new();

    // Map: chatId → { userId → typing expires at }
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, DateTimeOffset>> _typingByChat = new();

    public Task<IReadOnlySet<Guid>> GetOnlineUserIdsAsync(IReadOnlySet<Guid> userIds)
    {
        var online = new HashSet<Guid>();
        foreach (var id in userIds)
        {
            if (_onlineUsers.ContainsKey(id))
                online.Add(id);
        }
        return Task.FromResult<IReadOnlySet<Guid>>(online);
    }

    public Task<Dictionary<Guid, HashSet<Guid>>> GetTypingStatusAsync(IReadOnlyCollection<Guid> chatIds)
    {
        // Только запрошенные чаты: вызывающий передаёт чаты пользователя, и чужие
        // не читаются вовсе (раньше перебиралась вся карта, фильтр был снаружи).
        var now = _time.GetUtcNow();
        var result = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var chatId in chatIds)
        {
            if (!_typingByChat.TryGetValue(chatId, out var users))
                continue;

            var typing = new HashSet<Guid>();
            foreach (var (userId, expiresAt) in users)
            {
                if (expiresAt > now)
                    typing.Add(userId);
                else
                    users.TryRemove(KeyValuePair.Create(userId, expiresAt)); // протухшее — чистим по пути
            }

            if (typing.Count > 0)
                result[chatId] = typing;
        }
        return Task.FromResult(result);
    }

    public Task<bool> SetUserOnlineStatusAsync(Guid userId, string connectionId, bool status)
        => Task.FromResult(status ? AddConnection(userId, connectionId) : RemoveConnection(userId, connectionId));

    /// <returns>True, если это первое соединение пользователя (он стал онлайн).</returns>
    private bool AddConnection(Guid userId, string connectionId)
    {
        while (true)
        {
            var connections = _onlineUsers.GetOrAdd(userId, _ => new Connections());
            lock (connections)
            {
                if (connections.Removed)
                    continue; // проиграли гонку последнему отключению — берём свежий набор

                var isFirst = connections.Ids.Count == 0;
                connections.Ids.Add(connectionId);
                return isFirst;
            }
        }
    }

    /// <returns>True, если это было последнее соединение (пользователь ушёл в офлайн).</returns>
    private bool RemoveConnection(Guid userId, string connectionId)
    {
        if (!_onlineUsers.TryGetValue(userId, out var connections))
            return false;

        lock (connections)
        {
            if (connections.Removed || !connections.Ids.Remove(connectionId) || connections.Ids.Count > 0)
                return false;

            connections.Removed = true;
            _onlineUsers.TryRemove(KeyValuePair.Create(userId, connections));
            return true;
        }
    }

    public Task<int> GetConnectionCountAsync(Guid userId)
    {
        if (!_onlineUsers.TryGetValue(userId, out var connections))
            return Task.FromResult(0);

        lock (connections)
            return Task.FromResult(connections.Removed ? 0 : connections.Ids.Count);
    }

    public Task<bool> IsConnectionActiveAsync(Guid userId, string connectionId)
    {
        if (!_onlineUsers.TryGetValue(userId, out var connections))
            return Task.FromResult(false);

        lock (connections)
            return Task.FromResult(!connections.Removed && connections.Ids.Contains(connectionId));
    }

    public Task SetTypingAsync(Guid chatId, Guid userId, bool isTyping)
    {
        if (isTyping)
        {
            var users = _typingByChat.GetOrAdd(chatId, _ => new ConcurrentDictionary<Guid, DateTimeOffset>());
            users[userId] = _time.GetUtcNow() + TypingTtl;
        }
        else if (_typingByChat.TryGetValue(chatId, out var users))
        {
            users.TryRemove(userId, out _);
        }
        // Пустые словари чатов не удаляем: гонка удаления с параллельным Set потеряла бы
        // запись, а память ограничена числом чатов, где кто-то когда-то печатал.
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> ClearTypingAsync(Guid userId)
    {
        var now = _time.GetUtcNow();
        var chats = new List<Guid>();
        foreach (var (chatId, users) in _typingByChat)
        {
            if (users.TryRemove(userId, out var expiresAt) && expiresAt > now)
                chats.Add(chatId);
        }
        return Task.FromResult<IReadOnlyList<Guid>>(chats);
    }
}
