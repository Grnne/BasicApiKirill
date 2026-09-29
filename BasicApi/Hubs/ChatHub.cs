using System.Collections.Concurrent;
using System.Security.Claims;
using System.Threading.RateLimiting;
using BasicApi.Extensions;
using BasicApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BasicApi.Hubs;

/// <summary>
/// Адаптер SignalR: разбирает вызов, отдаёт его доменному сервису. Правила — в сервисах,
/// перевод доменных ошибок в <see cref="HubException"/> с кодом — в <see cref="HubErrorFilter"/>.
/// </summary>
[Authorize]
public class ChatHub(
    IMessageService messages,
    IPresenceService presence,
    IMembershipService membership,
    ISessionService sessions,
    HubConnectionRegistry connectionRegistry,
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
        if (UserId is { } userId)
        {
            // Сначала регистрируем, потом проверяем сессию: logout между этими
            // шагами либо найдёт соединение в реестре, либо проверка увидит отзыв.
            var sessionFamilyId = Context.User?.GetSessionFamilyId();
            connectionRegistry.Add(Context, userId, sessionFamilyId);

            if (sessionFamilyId is not null &&
                !await sessions.IsSessionFamilyLiveAsync(sessionFamilyId.Value, Context.ConnectionAborted))
            {
                // Access-токен ещё не истёк, но вход уже закрыт (logout, logout-all).
                logger.LogDebug("Rejected hub connection of revoked session: userId={UserId}", userId);
                connectionRegistry.Remove(Context.ConnectionId);
                Context.Abort();
                return;
            }

            await presence.ConnectedAsync(userId, Context.ConnectionId, Context.ConnectionAborted);
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            if (exception != null)
                logger.LogWarning(exception, "OnDisconnectedAsync with exception for userId={UserId}, connectionId={ConnectionId}",
                    UserId, Context.ConnectionId);

            if (UserId is { } userId)
                await presence.DisconnectedAsync(userId, Context.ConnectionId);

            await base.OnDisconnectedAsync(exception);
        }
        finally
        {
            if (Context.ConnectionId is not null)
            {
                connectionRegistry.Remove(Context.ConnectionId);
                if (ConnectionLimiters.TryRemove(Context.ConnectionId, out var limiter))
                    limiter.Dispose();
            }
        }
    }

    public async Task JoinChat(Guid chatId)
    {
        if (UserId is not { } userId) return;

        await membership.EnsureMemberAsync(chatId, userId, Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, chatId.ToString());
    }

    /// <summary>Выход из группы чата безвреден и без проверки членства.</summary>
    public Task LeaveChat(Guid chatId) =>
        UserId is null ? Task.CompletedTask : Groups.RemoveFromGroupAsync(Context.ConnectionId, chatId.ToString());

    public async Task SendMessage(Guid chatId, string text)
    {
        if (!TryAcquireCallSlot())
            throw HubErrors.Create(HubErrors.RateLimited, "Too many calls. Slow down.");

        if (UserId is not { } userId) return;

        await messages.SendAsync(chatId, userId, text, Context.ConnectionAborted);
    }

    public async Task Ping()
    {
        if (UserId is not { } userId) return;

        var info = await presence.GetConnectionInfoAsync(userId, Context.ConnectionId);

        await Clients.Caller.SendAsync("Pong", new
        {
            ConnectionId = Context.ConnectionId,
            ConnectionCount = info.ConnectionCount,
            IsCurrentActive = info.IsCurrentActive,
            UserId = userId
        });
    }

    public async Task Typing(Guid chatId, bool isTyping)
    {
        if (!TryAcquireCallSlot())
            return; // Typing — не критично, просто тихо игнорируем лишние вызовы.

        if (UserId is not { } userId) return;

        await presence.SetTypingAsync(chatId, userId, isTyping, Context.ConnectionAborted);
    }

    private Guid? UserId =>
        Guid.TryParse(Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId) ? userId : null;
}
