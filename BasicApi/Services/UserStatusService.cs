using System.Collections.Concurrent;

namespace BasicApi.Services;

/// <summary>
/// In-memory tracker of online and typing status; a per-user lock keeps connect/disconnect races from
/// losing a connection. NOTE: for horizontal scaling, replace with Redis.
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
    /// Connections of a single user. All changes are under a lock on this object
    /// (a per-user lock: different users do not get in each other's way).
    /// </summary>
    private sealed class Connections
    {
        public readonly HashSet<string> Ids = [];

        /// <summary>
        /// The set has already been removed from the dictionary (the last connection left). A connection
        /// that managed to grab a reference to it must take a new one - otherwise it would end up
        /// in an "orphaned" set, and the user would look offline while actually being connected.
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
        // Only the requested chats: the caller passes the user's chats, and others' are not read at all.
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
                    users.TryRemove(KeyValuePair.Create(userId, expiresAt)); // expired - clean it up along the way
            }

            if (typing.Count > 0)
                result[chatId] = typing;
        }
        return Task.FromResult(result);
    }

    public Task<bool> SetUserOnlineStatusAsync(Guid userId, string connectionId, bool status)
        => Task.FromResult(status ? AddConnection(userId, connectionId) : RemoveConnection(userId, connectionId));

    /// <returns>True if this is the user's first connection (they came online).</returns>
    private bool AddConnection(Guid userId, string connectionId)
    {
        while (true)
        {
            var connections = _onlineUsers.GetOrAdd(userId, _ => new Connections());
            lock (connections)
            {
                if (connections.Removed)
                    continue; // lost the race to the last disconnect - take the fresh set

                var isFirst = connections.Ids.Count == 0;
                connections.Ids.Add(connectionId);
                return isFirst;
            }
        }
    }

    /// <returns>True if this was the last connection (the user went offline).</returns>
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
        // We do not remove empty chat dictionaries: a race between removal and a parallel Set would lose
        // the entry, and memory is bounded by the number of chats where anyone has ever typed.
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
