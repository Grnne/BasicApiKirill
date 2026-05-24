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
    /// Returns a map of chatId → set of userIds currently typing in that chat,
    /// for all chats the given user is a member of.
    /// </summary>
    Task<Dictionary<Guid, HashSet<Guid>>> GetTypingStatusAsync(Guid userId);

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
    /// Updates typing state for a user in a chat.
    /// </summary>
    Task SetTypingAsync(Guid chatId, Guid userId, bool isTyping);
}
