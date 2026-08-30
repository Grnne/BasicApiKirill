using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;

namespace BasicApi.Storage.Interfaces;

public interface IChatRepository
{
    Task<IEnumerable<Chat>> GetUserChatsAsync(Guid userId);
    Task<List<ChatListResult>> GetUserChatsBatchedAsync(Guid userId);

    /// <summary>
    /// Returns a single chat-list row for one chat, as seen by the given user
    /// (companion, unread count and last message are resolved for that viewer).
    /// Returns null when the chat does not exist or the user is not a member.
    /// </summary>
    Task<ChatListResult?> GetChatListItemAsync(Guid chatId, Guid userId);
    Task<Chat?> GetByIdAsync(Guid chatId);
    Task<Chat?> GetPrivateChatAsync(Guid userId1, Guid userId2);
    Task<Guid> CreateAsync(Chat chat, Guid[] memberIds);
    Task<bool> IsMemberAsync(Guid chatId, Guid userId);
    Task<int> GetUnreadCountAsync(Guid chatId, Guid userId);
    Task<string?> GetCompanionNameAsync(Guid chatId, Guid userId);
    Task<string> GetUserNameAsync(Guid userId);
    Task<List<ChatParticipantDto>> GetChatParticipantsAsync(Guid chatId);

        /// <summary>
    /// Returns all unique member IDs across all chats the user participates in.
    /// </summary>
    Task<List<Guid>> GetAllChatMembersAsync(Guid userId);
    /// <summary>
    /// Searches user's chats by query and type.
    /// When query is null, returns all user chats (no filter).
    /// When query is provided, searches by ILIKE:
    ///   - type="group": matches chat title
    ///   - type="private": matches companion display_name or username
    ///   - type is null/empty: matches both (no type filter, but query applies to both)
    /// </summary>
    Task<List<ChatListResult>> SearchChatsBatchedAsync(Guid userId, string? query, string? typeFilter, int? limit);

    /// <summary>
    /// Returns total count of user's chats matching a search query and type.
    /// Same filtering logic as <see cref="SearchChatsBatchedAsync"/>.
    /// </summary>
    Task<int> CountChatsByQueryAsync(Guid userId, string? query, string? typeFilter);
}

