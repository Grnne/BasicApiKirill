using BasicApi.Middleware;
using BasicApi.Models.Dto.Message;
using BasicApi.Services;
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
            return new BadRequestObjectResult(new ProblemDetails
            {
                Title = "Bad Request",
                Status = StatusCodes.Status400BadRequest,
                Detail = "Cannot create chat with yourself",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1"
            });

        var existingChat = await chatRepository.GetPrivateChatAsync(currentUserId, otherUserId);

        if (existingChat != null)
            return new OkObjectResult(new { chatId = existingChat.Id, message = "Chat already exists" });

        var chat = new Chat
        {
            Id = Guid.NewGuid(),
            Type = "private",
            Title = null,
            CreatedAt = DateTime.UtcNow
        };

        var memberIds = new[] { currentUserId, otherUserId };
        var chatId = await chatRepository.CreateAsync(chat, memberIds);

        return new CreatedAtActionResult(
            actionName: nameof(GetChatAsync),
            controllerName: null,
            routeValues: new { chatId },
            value: new { chatId });
    }

    public async Task<IActionResult> GetChatAsync(Guid chatId, Guid userId)
    {
        var chat = await chatService.GetChatDetailsAsync(chatId, userId);
        return new OkObjectResult(chat);
    }

    public async Task<IActionResult> GetMessagesAsync(Guid chatId, Guid userId, DateTime? before, int limit)
    {
        var messages = await chatService.GetChatMessagesAsync(chatId, userId, before, limit);
        return new OkObjectResult(messages);
    }

    /// <summary>
    /// Cursor-based paginated messages endpoint.
    /// Exceptions (UnauthorizedAccessException, etc.) are handled by the global middleware.
    /// </summary>
    public async Task<IActionResult> GetMessagesCursorAsync(
        Guid chatId, Guid userId, string? cursor, int limit)
    {
        var result = await chatService.GetChatMessagesCursorAsync(chatId, userId, cursor, limit);
        return new OkObjectResult(result);
    }

    public async Task<IActionResult> MarkReadAsync(Guid chatId, Guid userId, Guid lastMessageId)
    {
        var isMember = await chatRepository.IsMemberAsync(chatId, userId);
        if (!isMember)
            return new ForbidResult();

        await messageRepository.UpdateLastReadAsync(chatId, userId, lastMessageId);
        return new OkResult();
    }
}