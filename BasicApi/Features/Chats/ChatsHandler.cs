using BasicApi.Hubs;
using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;
using BasicApi.Services;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace BasicApi.Features.Chats;

public class ChatsHandler(
    IChatService chatService,
    IChatRepository chatRepository,
    IUserRepository userRepository,
    IMessageRepository messageRepository,
    IHubContext<ChatHub> hubContext)
{
    public async Task<IActionResult> GetUserChatsAsync(Guid userId)
    {
        var chats = await chatService.GetUserChatsAsync(userId);
        return new OkObjectResult(chats);
    }

    public async Task<IActionResult> CreatePrivateChatAsync(Guid currentUserId, Guid otherUserId)
    {
        if (currentUserId == otherUserId)
            throw new BadRequestException("Cannot create chat with yourself", "SELF_CHAT");

        // Без этой проверки несуществующий собеседник ронял вставку на внешнем ключе (500).
        // Деактивированный — тоже 404: писать ему некому.
        var other = await userRepository.GetByIdAsync(otherUserId);
        if (other is null || !other.IsActive)
            throw new NotFoundException("User not found", "USER_NOT_FOUND");

        var (chatId, created) = await chatRepository.GetOrCreatePrivateChatAsync(currentUserId, otherUserId);

        if (!created)
            return new OkObjectResult(await BuildChatListItemAsync(chatId, currentUserId));

        // Уведомляем второго участника о новом чате через SignalR.
        // Payload собирается ОТДЕЛЬНО для него: собеседник в его карточке — создатель чата.
        var recipientRow = await chatRepository.GetChatListItemAsync(chatId, otherUserId);
        if (recipientRow is not null)
            await ChatHub.NotifyChatCreatedAsync(hubContext, otherUserId, ChatListItemMapper.Map(recipientRow));

        return new CreatedResult(string.Empty, await BuildChatListItemAsync(chatId, currentUserId));
    }

    /// <summary>
    /// Собирает элемент списка чатов для конкретного зрителя.
    /// Проверка членства здесь не нужна: вызывается только для чатов,
    /// участником которых пользователь заведомо является.
    /// </summary>
    private async Task<ChatListItemDto> BuildChatListItemAsync(Guid chatId, Guid userId)
    {
        var row = await chatRepository.GetChatListItemAsync(chatId, userId)
            ?? throw new NotFoundException("Chat not found", "CHAT_NOT_FOUND");

        return ChatListItemMapper.Map(row);
    }

    public async Task<IActionResult> GetChatAsync(Guid chatId, Guid userId)
    {
        var chat = await chatService.GetChatDetailsAsync(chatId, userId);
        return new OkObjectResult(chat);
    }

    /// <summary>
    /// Returns one chat in list-item shape (same DTO as GET /api/chats).
    /// </summary>
    public async Task<IActionResult> GetChatListItemAsync(Guid chatId, Guid userId)
    {
        var item = await chatService.GetChatListItemAsync(chatId, userId);
        return new OkObjectResult(item);
    }

    /// <summary>
    /// Cursor-based paginated messages endpoint.
    /// Exceptions are handled by the global middleware.
    /// </summary>
    public async Task<IActionResult> GetMessagesCursorAsync(
        Guid chatId, Guid userId, string? cursor, int limit)
    {
        var result = await chatService.GetChatMessagesCursorAsync(chatId, userId, cursor, limit);
        return new OkObjectResult(result);
    }

        /// <summary>
    /// Jump to messages around a specific date.
    /// Finds the nearest message at or before the given date and returns a page around it.
    /// Returns a CursorPaginatedResponse — use nextCursor to scroll further back.
    /// Authorization is handled inside ChatService.GetChatMessagesCursorAsync.
    /// </summary>
    public async Task<IActionResult> GetMessagesAtAsync(
        Guid chatId, Guid userId, DateTime date, int limit)
    {
        // Find the most recent message at or before the requested date
        var pivot = await messageRepository.GetFirstMessageBeforeDateAsync(chatId, date);

        // If no messages before this date, return the most recent page (cursor = null)
        string? cursor = pivot is not null
            ? new CursorDto(pivot.CreatedAt, pivot.Id).Encode()
            : null;

        var result = await chatService.GetChatMessagesCursorAsync(chatId, userId, cursor, limit);
        return new OkObjectResult(result);
    }

        public async Task<IActionResult> MarkReadAsync(Guid chatId, Guid userId, Guid lastMessageId)
    {
        var isMember = await chatRepository.IsMemberAsync(chatId, userId);
                if (!isMember)
            throw new ForbiddenException("User is not a member of this chat", "NOT_A_MEMBER");

        // Сообщение из чужого чата — 404: иначе указатель мог бы встать на чужое
        // сообщение, и счётчик непрочитанных сломался бы. Попытка отмотать назад —
        // не ошибка, просто ничего не меняется.
        var update = await messageRepository.MarkReadAsync(chatId, userId, lastMessageId);
        if (update == ReadPointerUpdate.MessageNotFound)
            throw new NotFoundException("Message not found in this chat", "MESSAGE_NOT_FOUND");

        return new OkResult();
    }

        /// <summary>
    /// Full-text search for messages within a chat.
    /// Validates the search query and delegates to the service layer.
        /// </summary>
    public async Task<IActionResult> SearchMessagesAsync(
        Guid chatId, Guid userId, string query, string? cursor, int limit)
    {
        var result = await chatService.SearchChatMessagesCursorAsync(chatId, userId, query, cursor, limit);
        return new OkObjectResult(result);
    }

    /// <summary>
    /// Searches user's chats by query.
    /// Supports optional type filter (group/private).
    /// </summary>
    public async Task<IActionResult> SearchChatsAsync(Guid userId, string query, string? type, int limit)
    {
        var result = await chatService.SearchChatsAsync(userId, query, type, limit);
        return new OkObjectResult(result);
    }
}