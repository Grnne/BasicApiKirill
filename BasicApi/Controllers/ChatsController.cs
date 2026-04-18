using BasicApi.Extensions;
using BasicApi.Models.Dto.Message;
using BasicApi.Services;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BasicApi.Controllers;

[Authorize]
[ApiController]
[Route("api/chats")]
public class ChatsController(
    IChatService chatService,
    IChatRepository chatRepository,
    IMessageRepository messageRepository) : ControllerBase
{

    // GET /api/chats - список чатов текущего пользователя
    [HttpGet]
    public async Task<IActionResult> GetUserChats()
    {
        var userId = User.GetUserId();
        var chats = await chatService.GetUserChatsAsync(userId);
        return Ok(chats);
    }

    // POST /api/chats/private/{userId} - создать или найти личку с пользователем
    [HttpPost("private/{userId}")]
    public async Task<IActionResult> CreatePrivateChat(Guid userId)
    {
        var currentUserId = User.GetUserId();

        if (currentUserId == userId)
            return BadRequest(new { message = "Cannot create chat with yourself" });

        var existingChat = await chatRepository.GetPrivateChatAsync(currentUserId, userId);

        if (existingChat != null)
            return Ok(new { chatId = existingChat.Id, message = "Chat already exists" });

        var chat = new Chat
        {
            Id = Guid.NewGuid(),
            Type = "private",
            Title = null,
            CreatedAt = DateTime.UtcNow
        };

        var memberIds = new[] { currentUserId, userId };
        var chatId = await chatRepository.CreateAsync(chat, memberIds);

        return CreatedAtAction(nameof(GetChat), new { chatId }, new { chatId });
    }

    // GET /api/chats/{chatId} - детали чата
    [HttpGet("{chatId}")]
    public async Task<IActionResult> GetChat(Guid chatId)
    {
        var userId = User.GetUserId();
        var chat = await chatService.GetChatDetailsAsync(chatId, userId);
        return Ok(chat);
    }

    // GET /api/chats/{chatId}/messages - история сообщений
    [HttpGet("{chatId}/messages")]
    public async Task<IActionResult> GetMessages(Guid chatId, [FromQuery] DateTime? before, [FromQuery] int limit = 50)
    {
        var userId = User.GetUserId();
        var messages = await chatService.GetChatMessagesAsync(chatId, userId, before, limit);
        return Ok(messages);
    }

    // POST /api/chats/{chatId}/read - отметить сообщения прочитанными
    [HttpPost("{chatId}/read")]
    public async Task<IActionResult> MarkRead(Guid chatId, [FromBody] MarkMessageReadDto dto)
    {
        var userId = User.GetUserId();

        var isMember = await chatRepository.IsMemberAsync(chatId, userId);
        if (!isMember)
            return Forbid();

        await messageRepository.UpdateLastReadAsync(chatId, userId, dto.LastMessageId);

        return Ok();
    }
}