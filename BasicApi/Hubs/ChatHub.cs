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
    // Хранит userId → набор connectionId для отслеживания онлайн-статуса и диагностики.
    // Один пользователь может иметь несколько активных соединений (несколько вкладок, устройств).
    // ConcurrentDictionary — thread-safe, поддерживает множественные одновременные подключения.
    // ВАЖНО: При горизонтальном масштабировании (несколько инстансов) необходимо заменить
    // на Redis backplane или SignalR Redis Scaleout, т.к. статический словарь не шарится между серверами.
    private static readonly ConcurrentDictionary<Guid, ConcurrentBag<string>> _onlineUsers = new();

    public override async Task OnConnectedAsync()
    {
        try
        {
            logger.LogInformation("OnConnectedAsync: new connection {ConnectionId}", Context.ConnectionId);
            var userId = GetUserId();
            if (userId.HasValue)
            {
                _onlineUsers.AddOrUpdate(
                    userId.Value,
                    _ => new ConcurrentBag<string> { Context.ConnectionId },
                    (_, bag) => { bag.Add(Context.ConnectionId); return bag; });

                var connectionCount = _onlineUsers[userId.Value].Count;
                logger.LogInformation("User {UserId} connected. ConnectionId: {ConnectionId}. Total connections: {Count}",
                    userId.Value, Context.ConnectionId, connectionCount);

                if (connectionCount == 1)
                {
                    var allChatMembers = await chatRepository.GetAllChatMembersAsync(userId.Value);
                    foreach (var memberId in allChatMembers)
                    {
                        await Clients.User(memberId.ToString()).SendAsync("UserOnlineChanged", userId.Value, true);
                    }
                    logger.LogInformation("User {UserId} online status sent to {Count} members", userId.Value, allChatMembers.Count());
                }
            }
            else
            {
                logger.LogWarning("OnConnectedAsync: anonymous connection rejected");
            }
            await base.OnConnectedAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OnConnectedAsync failed for connection {ConnectionId}", Context.ConnectionId);
            throw;
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            logger.LogInformation("OnDisconnectedAsync: connection {ConnectionId} exception={Exception}", Context.ConnectionId, exception?.Message);
            var userId = GetUserId();
            if (userId.HasValue)
            {
                logger.LogInformation("User {UserId} disconnected. ConnectionId: {ConnectionId}", userId.Value, Context.ConnectionId);

                if (_onlineUsers.TryGetValue(userId.Value, out var connections))
                {
                    // ConcurrentBag doesn't support removal, so we rebuild
                    var remaining = new ConcurrentBag<string>(
                        connections.Where(c => c != Context.ConnectionId));

                    if (remaining.IsEmpty)
                    {
                        _onlineUsers.TryRemove(userId.Value, out _);

                        var allChatMembers = await chatRepository.GetAllChatMembersAsync(userId.Value);
                        foreach (var memberId in allChatMembers)
                        {
                            await Clients.User(memberId.ToString()).SendAsync("UserOnlineChanged", userId.Value, false);
                        }
                        logger.LogInformation("User {UserId} offline status sent to {Count} members", userId.Value, allChatMembers.Count());
                    }
                    else
                    {
                        _onlineUsers[userId.Value] = remaining;
                        logger.LogInformation("User {UserId} has {Count} remaining connections", userId.Value, remaining.Count);
                    }
                }
            }
            await base.OnDisconnectedAsync(exception);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OnDisconnectedAsync failed for connection {ConnectionId}", Context.ConnectionId);
            throw;
        }
    }

    // Подписка на группу чата
    public async Task JoinChat(Guid chatId)
    {
        logger.LogInformation("JoinChat called with chatId={ChatId}", chatId);
        try
        {
            var userId = GetUserId();
            if (!userId.HasValue)
            {
                logger.LogWarning("JoinChat rejected: no userId in token");
                return;
            }

            logger.LogInformation("JoinChat: user {UserId} checking membership for chat {ChatId}", userId.Value, chatId);
            var isMember = await chatRepository.IsMemberAsync(chatId, userId.Value);
            if (!isMember)
            {
                logger.LogWarning("User {UserId} tried to join chat {ChatId} but is not a member", userId.Value, chatId);
                return;
            }

            logger.LogInformation("User {UserId} joined chat {ChatId}", userId.Value, chatId);
            await Groups.AddToGroupAsync(Context.ConnectionId, chatId.ToString());
            logger.LogInformation("User {UserId} successfully added to group {ChatId}", userId.Value, chatId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "JoinChat failed for chatId={ChatId}", chatId);
            throw;
        }
    }

    // Отписка от группы чата
    public async Task LeaveChat(Guid chatId)
    {
        try
        {
            var userId = GetUserId();
            if (!userId.HasValue) return;

            var isMember = await chatRepository.IsMemberAsync(chatId, userId.Value);
            if (!isMember) return;

            logger.LogInformation("User {UserId} left chat {ChatId}", userId, chatId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, chatId.ToString());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "LeaveChat failed for chatId={ChatId}", chatId);
            throw;
        }
    }

    // Отправка сообщения
    public async Task SendMessage(Guid chatId, string text)
    {
        try
        {
            var userId = GetUserId();
            if (!userId.HasValue)
            {
                logger.LogWarning("SendMessage rejected: no userId in token");
                return;
            }

            logger.LogInformation("SendMessage: user {UserId} sending to chat {ChatId}", userId.Value, chatId);

            var isMember = await chatRepository.IsMemberAsync(chatId, userId.Value);
            if (!isMember)
            {
                logger.LogWarning("User {UserId} tried to send message to chat {ChatId} but is not a member", userId.Value, chatId);
                return;
            }

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

            logger.LogInformation("Message {MessageId} saved to DB", message.Id);

            var senderName = await chatRepository.GetUserNameAsync(userId.Value);

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
            logger.LogInformation("Message {MessageId} sent to chat group {ChatId}", message.Id, chatId);

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

                        int notified = 0;
            foreach (var participant in participants)
            {
                await Clients.User(participant.UserId.ToString())
                    .SendAsync("ChatListUpdated", chatId, listUpdateDto);
                notified++;
            }
            logger.LogInformation("ChatListUpdated sent to {Count} participants of chat {ChatId}", notified, chatId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SendMessage failed for chatId={ChatId}", chatId);
            throw;
        }
    }

    /// <summary>
    /// Диагностический метод — возвращает количество активных SignalR-соединений для текущего пользователя.
    /// </summary>
    public async Task Ping()
    {
        var userId = GetUserId();
        if (!userId.HasValue) return;

        var connectionCount = 0;
        if (_onlineUsers.TryGetValue(userId.Value, out var connections))
        {
            connectionCount = connections.Count;
        }

        logger.LogInformation("Ping: user {UserId} has {Count} active connections", userId.Value, connectionCount);
        await Clients.Caller.SendAsync("Pong", new { ConnectionId = Context.ConnectionId, ConnectionCount = connectionCount });
    }

    public async Task Typing(Guid chatId, bool isTyping)
    {
        try
        {
            var userId = GetUserId();
            if (!userId.HasValue)
            {
                logger.LogWarning("Typing rejected: no userId in token");
                return;
            }

            var participants = await chatRepository.GetChatParticipantsAsync(chatId);
            foreach (var participant in participants)
            {
                if (participant.UserId == userId) continue;

                await Clients.User(participant.UserId.ToString())
                    .SendAsync("TypingChanged", chatId, userId.Value, isTyping);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Typing failed for chatId={ChatId}", chatId);
            throw;
        }
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