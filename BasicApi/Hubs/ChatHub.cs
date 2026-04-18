using System.Security.Claims;
using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BasicApi.Hubs;

[Authorize]
public class ChatHub(IChatRepository chatRepository, IMessageRepository messageRepository) : Hub
{
    private static readonly Dictionary<Guid, string> _onlineUsers = []; // userId -> connectionId

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        if (userId.HasValue)
        {
            _onlineUsers[userId.Value] = Context.ConnectionId;
            await Clients.All.SendAsync("UserOnlineChanged", userId.Value, true);
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId();
        if (userId.HasValue)
        {
            _onlineUsers.Remove(userId.Value);
            await Clients.All.SendAsync("UserOnlineChanged", userId.Value, false);
        }
        await base.OnDisconnectedAsync(exception);
    }

    // Подписка на группу чата
    public async Task JoinChat(Guid chatId)
    {
        var userId = GetUserId();
        if (!userId.HasValue) return;

        var isMember = await chatRepository.IsMemberAsync(chatId, userId.Value);
        if (!isMember) return;

        await Groups.AddToGroupAsync(Context.ConnectionId, chatId.ToString());
    }

    // Отписка от группы чата
    public async Task LeaveChat(Guid chatId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, chatId.ToString());
    }

    // Отправка сообщения
    public async Task SendMessage(Guid chatId, string text)
    {
        var userId = GetUserId();
        if (!userId.HasValue) return;

        var isMember = await chatRepository.IsMemberAsync(chatId, userId.Value);
        if (!isMember) return;

        // Сохраняем сообщение в БД
        var message = new Message
        {
            Id = Guid.NewGuid(),
            ChatId = chatId,
            SenderId = userId.Value,
            Text = text,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        };

        await messageRepository.CreateAsync(message);

        // Получаем имя отправителя
        var senderName = await chatRepository.GetUserNameAsync(userId.Value);

        // Отправляем сообщение всем в группе чата
        var messageDto = new MessageDto
        {
            Id = message.Id,
            SenderId = message.SenderId,
            SenderName = senderName,
            Text = message.Text,
            CreatedAt = message.CreatedAt,
            IsRead = false
        };

        await Clients.Group(chatId.ToString()).SendAsync("MessageCreated", messageDto);
    }

    // Статус печатания
    public async Task Typing(Guid chatId, bool isTyping)
    {
        var userId = GetUserId();
        if (!userId.HasValue) return;

        await Clients.Group(chatId.ToString()).SendAsync("TypingChanged", chatId, userId.Value, isTyping);
    }

    private Guid? GetUserId()
    {
        var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(userIdClaim, out var userId))
            return userId;
        return null;
    }
}