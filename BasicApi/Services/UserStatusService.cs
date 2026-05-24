using System.Collections.Concurrent;

namespace BasicApi.Services;

/// <summary>
/// In-memory tracker for user online/offline and typing status.
/// Thread-safe via ConcurrentDictionary and immutable snapshots.
/// NOTE: For horizontal scaling, replace with Redis or SignalR Redis backplane.
/// </summary>
public class UserStatusService : IUserStatusService
{
    // Map: userId → { connectionId → byte }
    // ConcurrentDictionary<string, byte> is used as a thread-safe set of connection IDs
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> _onlineUsers = new();

    // Map: chatId (string) → { userId → byte }
    // ConcurrentDictionary<Guid, byte> is used as a thread-safe set of userIds typing in that chat
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, byte>> _typingByChat = new();

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

    public Task<Dictionary<Guid, HashSet<Guid>>> GetTypingStatusAsync(Guid userId)
    {
        // Returns a snapshot of all typing statuses.
        // Filtering by user's chats is done in the handler layer.
        var result = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var kvp in _typingByChat)
        {
            if (kvp.Value.Count > 0)
            {
                var chatId = Guid.Parse(kvp.Key);
                result[chatId] = [.. kvp.Value.Keys];
            }
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
        var key = chatId.ToString();
        if (isTyping)
        {
            var userSet = _typingByChat.GetOrAdd(key, _ => new ConcurrentDictionary<Guid, byte>());
            userSet.TryAdd(userId, 0);
        }
        else
        {
            if (_typingByChat.TryGetValue(key, out var userSet))
            {
                userSet.TryRemove(userId, out _);
                if (userSet.IsEmpty)
                {
                    _typingByChat.TryRemove(KeyValuePair.Create(key, userSet));
                }
            }
        }
        return Task.CompletedTask;
    }
}
