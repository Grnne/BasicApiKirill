using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;

namespace BasicApi.Services;

public interface IChatService
{
    Task<List<ChatListItemDto>> GetUserChatsAsync(Guid userId);
    Task<ChatDetailDto> GetChatDetailsAsync(Guid chatId, Guid userId);
    Task<List<MessageDto>> GetChatMessagesAsync(Guid chatId, Guid userId, DateTime? before, int limit);

    /// <summary>
    /// Returns messages with cursor-based pagination.
    /// </summary>
    Task<CursorPaginatedResponse<MessageDto>> GetChatMessagesCursorAsync(
        Guid chatId, Guid userId, string? cursor, int limit);
}