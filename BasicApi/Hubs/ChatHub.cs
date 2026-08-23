using System.Collections.Concurrent;
using System.Security.Claims;
using System.Threading.RateLimiting;
using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;
using BasicApi.Services;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BasicApi.Hubs;

[Authorize]
public class ChatHub(
    IChatRepository chatRepository,
    IMessageRepository messageRepository,
    IUserStatusService userStatusService,
    ILogger<ChatHub> logger) : Hub
{
    // Троттлинг вызовов на соединение — защита от спама SendMessage/Typing,
    // которые дороже обычного REST-запроса (пишут в БД и рассылают всем участникам чата).
    private static readonly ConcurrentDictionary<string, RateLimiter> ConnectionLimiters = new();

    private bool TryAcquireCallSlot()
    {
        var connectionId = Context.ConnectionId ?? string.Empty;
        var limiter = ConnectionLimiters.GetOrAdd(connectionId, _ => new FixedWindowRateLimiter(
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromSeconds(10),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

        using var lease = limiter.AttemptAcquire();
        return lease.IsAcquired;
    }

    public override async Task OnConnectedAsync()
    {
        try
        {
            var userId = GetUserId();
            if (userId.HasValue)
            {
                var httpCtx = Context.GetHttpContext();
                var userAgent = httpCtx?.Request.Headers.UserAgent.FirstOrDefault() ?? "unknown";
                var remoteIp = httpCtx?.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                logger.LogInformation(
                    "OnConnectedAsync: userId={UserId}, connectionId={ConnectionId}, ip={RemoteIp}, userAgent={UserAgent}",
                    userId.Value, Context.ConnectionId, remoteIp, userAgent);

                var isFirstConnection = await userStatusService.SetUserOnlineStatusAsync(userId.Value, Context.ConnectionId, true);
                var totalConnections = await userStatusService.GetConnectionCountAsync(userId.Value);

                logger.LogInformation(
                    "User {UserId} now has {Count} active connections (just added {ConnectionId})",
                    userId.Value, totalConnections, Context.ConnectionId);

                if (isFirstConnection)
                {
                    var members = await chatRepository.GetAllChatMembersAsync(userId.Value);
                    foreach (var memberId in members)
                        await Clients.User(memberId.ToString()).SendAsync("UserOnlineChanged", userId.Value, true);
                }
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
            var userId = GetUserId();

            if (exception != null)
                logger.LogWarning(exception, "OnDisconnectedAsync with exception for userId={UserId}, connectionId={ConnectionId}",
                    userId, Context.ConnectionId);

            if (userId.HasValue)
            {
                var beforeCount = await userStatusService.GetConnectionCountAsync(userId.Value);
                var wentOffline = await userStatusService.SetUserOnlineStatusAsync(userId.Value, Context.ConnectionId, false);
                var afterCount = await userStatusService.GetConnectionCountAsync(userId.Value);

                logger.LogInformation(
                    "OnDisconnectedAsync: userId={UserId}, connectionId={ConnectionId}, beforeCount={BeforeCount}, afterCount={AfterCount}",
                    userId.Value, Context.ConnectionId, beforeCount, afterCount);

                if (wentOffline)
                {
                    var members = await chatRepository.GetAllChatMembersAsync(userId.Value);
                    foreach (var memberId in members)
                        await Clients.User(memberId.ToString()).SendAsync("UserOnlineChanged", userId.Value, false);
                }
            }
            await base.OnDisconnectedAsync(exception);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OnDisconnectedAsync failed for connection {ConnectionId}", Context.ConnectionId);
            throw;
        }
        finally
        {
            if (Context.ConnectionId is not null && ConnectionLimiters.TryRemove(Context.ConnectionId, out var limiter))
                limiter.Dispose();
        }
    }

    public async Task JoinChat(Guid chatId)
    {
        try
        {
            var userId = GetUserId();
            if (!userId.HasValue) return;

            if (!await chatRepository.IsMemberAsync(chatId, userId.Value))
                return;

            await Groups.AddToGroupAsync(Context.ConnectionId, chatId.ToString());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "JoinChat failed for chatId={ChatId}", chatId);
            throw;
        }
    }

    public async Task LeaveChat(Guid chatId)
    {
        try
        {
            var userId = GetUserId();
            if (!userId.HasValue) return;

            if (!await chatRepository.IsMemberAsync(chatId, userId.Value))
                return;

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, chatId.ToString());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "LeaveChat failed for chatId={ChatId}", chatId);
            throw;
        }
    }

    public async Task SendMessage(Guid chatId, string text)
    {
        try
        {
            if (!TryAcquireCallSlot())
                throw new HubException("Rate limit exceeded. Slow down.");

            var userId = GetUserId();
            if (!userId.HasValue) return;

            if (!await chatRepository.IsMemberAsync(chatId, userId.Value))
                return;

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
                await Clients.User(participant.UserId.ToString())
                    .SendAsync("ChatListUpdated", chatId, listUpdateDto);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SendMessage failed for chatId={ChatId}", chatId);
            throw;
        }
    }

    public async Task Ping()
    {
        var userId = GetUserId();
        if (!userId.HasValue) return;

        var connectionCount = await userStatusService.GetConnectionCountAsync(userId.Value);
        var isCurrentActive = await userStatusService.IsConnectionActiveAsync(userId.Value, Context.ConnectionId);

        await Clients.Caller.SendAsync("Pong", new
        {
            ConnectionId = Context.ConnectionId,
            ConnectionCount = connectionCount,
            IsCurrentActive = isCurrentActive,
            UserId = userId.Value
        });
    }

    public async Task Typing(Guid chatId, bool isTyping)
    {
        try
        {
            if (!TryAcquireCallSlot())
                return; // Typing — не критично, просто тихо игнорируем лишние вызовы.

            var userId = GetUserId();
            if (!userId.HasValue) return;

            await userStatusService.SetTypingAsync(chatId, userId.Value, isTyping);

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
    public static async Task NotifyChatCreatedAsync(
        IHubContext<ChatHub> hubContext,
        Guid chatId,
        ChatCreatedEventDto dto,
        Guid[] recipientIds)
    {
        foreach (var userId in recipientIds)
            await hubContext.Clients.User(userId.ToString()).SendAsync("ChatCreated", chatId, dto);
    }

    private Guid? GetUserId()
    {
        var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(userIdClaim, out var userId)) return userId;
        return null;
    }
}
