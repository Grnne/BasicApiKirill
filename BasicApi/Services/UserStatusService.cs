using System.Collections.Concurrent;

namespace BasicApi.Services;

/// <summary>
/// In-memory tracker for user online/offline and typing status.
/// Thread-safe via ConcurrentDictionary and immutable snapshots.
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

    // Map: userId → { connectionId → byte }
    // ConcurrentDictionary<string, byte> is used as a thread-safe set of connection IDs
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> _onlineUsers = new();

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
    {
        if (status)
        {
            // AddOrUpdate: creates a new inner dict if absent, or adds the connection.
            // The factory/addValueFactory run outside the dictionary lock,
            // but TryAdd/TryUpdate inside the inner dict are atomic.
            var connSet = _onlineUsers.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>());
            var isNew = connSet.IsEmpty;
            connSet.TryAdd(connectionId, 0);
            return Task.FromResult(isNew);
        }
        else
        {
            if (_onlineUsers.TryGetValue(userId, out var connSet))
            {
                connSet.TryRemove(connectionId, out _);
                if (connSet.IsEmpty)
                {
                    // Atomically remove the user only if no other connection was added concurrently
                    _onlineUsers.TryRemove(KeyValuePair.Create(userId, connSet));
                    // Double-check: if TryRemove failed, someone re-added a connection
                    return Task.FromResult(!_onlineUsers.ContainsKey(userId));
                }
            }
            return Task.FromResult(false);
        }
    }

    public Task<int> GetConnectionCountAsync(Guid userId)
    {
        if (_onlineUsers.TryGetValue(userId, out var connSet))
            return Task.FromResult(connSet.Count);
        return Task.FromResult(0);
    }

    public Task<bool> IsConnectionActiveAsync(Guid userId, string connectionId)
    {
        if (_onlineUsers.TryGetValue(userId, out var connSet))
            return Task.FromResult(connSet.ContainsKey(connectionId));
        return Task.FromResult(false);
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
