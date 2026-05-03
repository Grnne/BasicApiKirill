using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;

namespace BasicApi.Services;

public interface IChatService
{
    Task<List<ChatListItemDto>> GetUserChatsAsync(Guid userId);
    Task<ChatDetailDto> GetChatDetailsAsync(Guid chatId, Guid userId);

        /// <summary>
    /// Returns messages with cursor-based pagination.
    /// </summary>
    Task<CursorPaginatedResponse<MessageDto>> GetChatMessagesCursorAsync(
        Guid chatId, Guid userId, string? cursor, int limit);

    /// <summary>
    /// Full-text search for messages within a chat.
    /// Supports cursor-based pagination with (created_at, id) composite cursor.
    /// </summary>
    /// <param name="chatId">Chat to search in.</param>
    /// <param name="userId">Current user ID (for authorization).</param>
    /// <param name="query">Search query (min 2 characters).</param>
    /// <param name="cursor">Cursor from previous page (optional).</param>
        /// <param name="limit">Max results per page.</param>
    Task<SearchMessagesResponseDto> SearchChatMessagesCursorAsync(
        Guid chatId, Guid userId, string query, string? cursor, int limit);

    /// <summary>
    /// Searches user's chats by query.
    /// For type=group: searches by chat title (ILIKE).
    /// For type=private: searches by companion display name or username (ILIKE).
    /// If type is null/empty: searches both.
    /// </summary>
    /// <param name="userId">Current user ID.</param>
    /// <param name="query">Search query (min 1 character).</param>
    /// <param name="type">Optional filter: "group" or "private". Null/empty searches both.</param>
    /// <param name="limit">Max results.</param>
    Task<SearchChatsResponseDto> SearchChatsAsync(Guid userId, string query, string? type, int limit);
}