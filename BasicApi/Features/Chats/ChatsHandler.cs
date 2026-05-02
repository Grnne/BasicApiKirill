using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;
using BasicApi.Services;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BasicApi.Features.Chats;

public class ChatsHandler(
    IChatService chatService,
    IChatRepository chatRepository,
    IMessageRepository messageRepository)
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

        var existingChat = await chatRepository.GetPrivateChatAsync(currentUserId, otherUserId);

                if (existingChat != null)
            return new OkObjectResult(new CreateChatResponseDto { ChatId = existingChat.Id });

        var chat = new Chat
        {
            Id = Guid.NewGuid(),
            Type = "private",
            Title = null,
            CreatedAt = DateTime.UtcNow
        };

        var memberIds = new[] { currentUserId, otherUserId };
        var chatId = await chatRepository.CreateAsync(chat, memberIds);

                return new CreatedResult(string.Empty, new CreateChatResponseDto { ChatId = chatId });
    }

    public async Task<IActionResult> GetChatAsync(Guid chatId, Guid userId)
    {
        var chat = await chatService.GetChatDetailsAsync(chatId, userId);
        return new OkObjectResult(chat);
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

        await messageRepository.UpdateLastReadAsync(chatId, userId, lastMessageId);
        return new OkResult();
    }
}