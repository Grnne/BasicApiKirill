namespace BasicApi.Services;

/// <summary>Real-time user status (online, typing): the hub updates it, REST endpoints read it.</summary>
public interface IUserStatusService
{
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

    Task<int> GetConnectionCountAsync(Guid userId);

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
