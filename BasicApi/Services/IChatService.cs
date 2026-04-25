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
}