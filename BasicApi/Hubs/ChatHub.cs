using System.Collections.Concurrent;
using System.Security.Claims;
using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BasicApi.Hubs;

[Authorize]
public class ChatHub(IChatRepository chatRepository, IMessageRepository messageRepository, ILogger<ChatHub> logger) : Hub
{
    // Хранит userId → connectionId для отслеживания онлайн-статуса.
    // ConcurrentDictionary — thread-safe, поддерживает множественные одновременные подключения.
    // ВАЖНО: При горизонтальном масштабировании (несколько инстансов) необходимо заменить
    // на Redis backplane или SignalR Redis Scaleout, т.к. статический словарь не шарится между серверами.
        private static readonly ConcurrentDictionary<Guid, string> _onlineUsers = new();

        public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        if (userId.HasValue)
        {
            _onlineUsers.AddOrUpdate(userId.Value, Context.ConnectionId, (_, _) => Context.ConnectionId);
            logger.LogInformation("User {UserId} connected. ConnectionId: {ConnectionId}", userId.Value, Context.ConnectionId);

            var allChatMembers = await chatRepository.GetAllChatMembersAsync(userId.Value);
            foreach (var memberId in allChatMembers)
            {
                await Clients.User(memberId.ToString()).SendAsync("UserOnlineChanged", userId.Value, true);
            }
        }
        await base.OnConnectedAsync();
    }

        public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId();
        if (userId.HasValue)
        {
            logger.LogInformation("User {UserId} disconnected. ConnectionId: {ConnectionId}", userId.Value, Context.ConnectionId);
            if (_onlineUsers.TryRemove(userId.Value, out var removedId)
                && removedId == Context.ConnectionId)
            {
                if (!_onlineUsers.ContainsKey(userId.Value))
                {
                    var allChatMembers = await chatRepository.GetAllChatMembersAsync(userId.Value);
                    foreach (var memberId in allChatMembers)
                    {
                        await Clients.User(memberId.ToString()).SendAsync("UserOnlineChanged", userId.Value, false);
                    }
                }
            }
        }
        await base.OnDisconnectedAsync(exception);
    }

        // Подписка на группу чата
    public async Task JoinChat(Guid chatId)
    {
        var userId = GetUserId();
        if (!userId.HasValue) return;

        var isMember = await chatRepository.IsMemberAsync(chatId, userId.Value);
        if (!isMember)
        {
            logger.LogWarning("User {UserId} tried to join chat {ChatId} but is not a member", userId.Value, chatId);
            return;
        }

        logger.LogInformation("User {UserId} joined chat {ChatId}", userId.Value, chatId);
        await Groups.AddToGroupAsync(Context.ConnectionId, chatId.ToString());
    }

    // Отписка от группы чата
    public async Task LeaveChat(Guid chatId)
    {
        var userId = GetUserId();
        logger.LogInformation("User {UserId} left chat {ChatId}", userId, chatId);
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

                // Уведомляем всех участников чата об обновлении последнего сообщения в списке чатов
        logger.LogInformation("Message {MessageId} sent to chat group {ChatId}", message.Id, chatId);
        // Даже те, кто не открывал чат (не вызывал JoinChat), получат ChatListUpdated
        var participants = await chatRepository.GetChatParticipantsAsync(chatId);
        var listUpdateDto = new MessageDto
        {
            Id = message.Id,
            SenderId = message.SenderId,
            SenderName = senderName,
            Text = text.Length > 100 ? text[..100] + "…" : text,
            CreatedAt = message.CreatedAt,
            IsRead = false
        };

                foreach (var participant in participants)
        {
            if (participant.UserId == userId) continue; // Отправителю не нужно обновлять список
            await Clients.User(participant.UserId.ToString())
                .SendAsync("ChatListUpdated", chatId, listUpdateDto);
        }
    }
    public async Task Typing(Guid chatId, bool isTyping)
    {
        var userId = GetUserId();
                if (!userId.HasValue) return;

        logger.LogInformation("User {UserId} typing={IsTyping} in chat {ChatId}", userId.Value, isTyping, chatId);
        await Clients.Group(chatId.ToString()).SendAsync("TypingChanged", chatId, userId.Value, isTyping);
    }

    /// <summary>
    /// Уведомляет указанных пользователей о новом чате через SignalR.
    /// Вызывается из REST-хендлеров после создания чата.
    /// </summary>
    /// <param name="hubContext">IHubContext для отправки событий.</param>
    /// <param name="chatId">ID созданного чата.</param>
    /// <param name="dto">Данные о чате (тип, название, имя собеседника).</param>
    /// <param name="recipientIds">ID пользователей, которым отправить событие.</param>
    public static async Task NotifyChatCreatedAsync(
        IHubContext<ChatHub> hubContext,
        Guid chatId,
        ChatCreatedEventDto dto,
        Guid[] recipientIds)
    {
        foreach (var userId in recipientIds)
        {
            await hubContext.Clients.User(userId.ToString())
                .SendAsync("ChatCreated", chatId, dto);
        }
    }

    private Guid? GetUserId()
    {
        var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(userIdClaim, out var userId))
            return userId;
        return null;
    }
}


