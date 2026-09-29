using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;

namespace BasicApi.Storage.Interfaces;

public interface IChatRepository
{
    /// <summary>Ids of the chats the user is a member of.</summary>
    Task<IReadOnlyList<Guid>> GetUserChatIdsAsync(Guid userId, CancellationToken ct = default);

    Task<List<ChatListResult>> GetUserChatsBatchedAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Returns a single chat-list row for one chat, as seen by the given user
    /// (companion, unread count and last message are resolved for that viewer).
    /// Returns null when the chat does not exist or the user is not a member.
    /// </summary>
    Task<ChatListResult?> GetChatListItemAsync(Guid chatId, Guid userId, CancellationToken ct = default);
    Task<Chat?> GetByIdAsync(Guid chatId, CancellationToken ct = default);

    /// <summary>
    /// Returns the private chat of two users, creating it if there is none.
    /// Atomic: concurrent calls for the same pair (in any order) end up with one chat,
    /// and exactly one of them reports <c>Created = true</c>.
    /// </summary>
    Task<(Guid ChatId, bool Created)> GetOrCreatePrivateChatAsync(
        Guid userId, Guid otherUserId, CancellationToken ct = default);

    Task<bool> IsMemberAsync(Guid chatId, Guid userId, CancellationToken ct = default);
    /// <summary>Ids of the chat's members; empty when the chat does not exist.</summary>
    Task<IReadOnlyList<Guid>> GetMemberIdsAsync(Guid chatId, CancellationToken ct = default);

    Task<List<ChatParticipantDto>> GetChatParticipantsAsync(Guid chatId, CancellationToken ct = default);

    /// <summary>
    /// Returns all unique member IDs across all chats the user participates in.
    /// </summary>
    Task<List<Guid>> GetAllChatMembersAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Searches user's chats by query and type.
    /// When query is null, returns all user chats (no filter).
    /// When query is provided, searches by ILIKE:
    ///   - type="group": matches chat title
    ///   - type="private": matches companion display_name or username
    ///   - type is null/empty: matches both (no type filter, but query applies to both)
    /// </summary>
    Task<List<ChatListResult>> SearchChatsBatchedAsync(
        Guid userId, string? query, string? typeFilter, int? limit, CancellationToken ct = default);

    /// <summary>
    /// Returns total count of user's chats matching a search query and type.
    /// Same filtering logic as <see cref="SearchChatsBatchedAsync"/>.
    /// </summary>
    Task<int> CountChatsByQueryAsync(Guid userId, string? query, string? typeFilter, CancellationToken ct = default);
}
