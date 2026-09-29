namespace BasicApi.Services;

/// <summary>
/// Tracks real-time user status (online/offline, typing).
/// Used by both SignalR hub (to update state) and REST endpoints (to query state).
/// </summary>
public interface IUserStatusService
{
    /// <summary>
    /// Returns which of the given user IDs are currently online.
    /// </summary>
    Task<IReadOnlySet<Guid>> GetOnlineUserIdsAsync(IReadOnlySet<Guid> userIds);

    /// <summary>
    /// Returns a map of chatId → set of userIds currently typing, for the given chats only.
    /// Chats with nobody typing are omitted. Entries older than the typing TTL are ignored.
    /// </summary>
    Task<Dictionary<Guid, HashSet<Guid>>> GetTypingStatusAsync(IReadOnlyCollection<Guid> chatIds);

    /// <summary>
    /// Marks a user as online or offline.
    /// When status=true, adds the connectionId. When status=false, removes it.
    /// </summary>
    /// <returns>True if the user's online status actually changed (first connection added / last connection removed).</returns>
    Task<bool> SetUserOnlineStatusAsync(Guid userId, string connectionId, bool status);

    /// <summary>
    /// Returns the number of active connections for a user.
    /// </summary>
    Task<int> GetConnectionCountAsync(Guid userId);

    /// <summary>
    /// Checks whether a specific connection is currently active for a user.
    /// </summary>
    Task<bool> IsConnectionActiveAsync(Guid userId, string connectionId);

    /// <summary>
    /// Updates typing state for a user in a chat. "Typing" expires on its own after
    /// the TTL unless refreshed, so a client that vanished mid-typing does not hang there.
    /// </summary>
    Task SetTypingAsync(Guid chatId, Guid userId, bool isTyping);

    /// <summary>
    /// Removes the user's typing state in every chat (the user went offline).
    /// </summary>
    /// <returns>Chats where the user was typing, so members can be told it stopped.</returns>
    Task<IReadOnlyList<Guid>> ClearTypingAsync(Guid userId);
}
