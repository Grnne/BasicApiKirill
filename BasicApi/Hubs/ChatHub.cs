using System.Collections.Concurrent;
using System.Security.Claims;
using System.Threading.RateLimiting;
using BasicApi.Extensions;
using BasicApi.Features.Auth;
using BasicApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BasicApi.Hubs;

/// <summary>
/// SignalR adapter: parses the call and hands it to the domain service. The rules live in the services,
/// translating domain errors into <see cref="HubException"/> with a code is done by <see cref="HubErrorFilter"/>.
/// </summary>
[Authorize]
public class ChatHub(
    IMessageService messages,
    IPresenceService presence,
    IChatPolicy policy,
    ISessionService sessions,
    HubConnectionRegistry connectionRegistry,
    ILogger<ChatHub> logger) : Hub
{
    // Per-connection call throttling - protection against SendMessage/Typing spam,
    // which are more expensive than a regular REST request (they write to the DB and broadcast to all chat members).
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
            // Register first, then check the session: a logout between these
            // steps will either find the connection in the registry, or the check will see the revocation.
            var sessionFamilyId = Context.User?.GetSessionFamilyId();
            connectionRegistry.Add(Context, userId, sessionFamilyId, Context.User?.GetTokenExpiry());

            try
            {
                if (sessionFamilyId is not null &&
                    !await sessions.IsSessionFamilyLiveAsync(sessionFamilyId.Value, Context.ConnectionAborted))
                {
                    // The access token has not expired yet, but the sign-in is already closed (logout, logout-all).
                    logger.LogDebug("Rejected hub connection of revoked session: userId={UserId}", userId);
                    connectionRegistry.Remove(Context.ConnectionId);
                    Context.Abort();
                    return;
                }

                await presence.ConnectedAsync(userId, Context.ConnectionId, Context.ConnectionAborted);
            }
            catch
            {
                // SignalR does not call OnDisconnectedAsync when OnConnectedAsync fails — typically the
                // client left while it ran. Without this the user stayed online until a restart.
                connectionRegistry.Remove(Context.ConnectionId);
                await presence.DisconnectedAsync(userId, Context.ConnectionId);
                throw;
            }
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

        await policy.DemandReadAsync(userId, chatId, Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, chatId.ToString());
    }

    /// <summary>Leaving a chat group is harmless even without a membership check.</summary>
    public Task LeaveChat(Guid chatId) =>
        UserId is null ? Task.CompletedTask : Groups.RemoveFromGroupAsync(Context.ConnectionId, chatId.ToString());

    public async Task SendMessage(Guid chatId, string text)
    {
        if (!TryAcquireCallSlot())
            throw HubErrors.Create(HubErrors.RateLimited, "Too many calls. Slow down.");

        if (UserId is not { } userId) return;

        await messages.SendAsync(chatId, userId, text, ct: Context.ConnectionAborted);
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
            return; // Typing is not critical, just silently ignore the extra calls.

        if (UserId is not { } userId) return;

        await presence.SetTypingAsync(chatId, userId, isTyping, Context.ConnectionAborted);
    }

    private Guid? UserId =>
        Guid.TryParse(Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId) ? userId : null;
}
