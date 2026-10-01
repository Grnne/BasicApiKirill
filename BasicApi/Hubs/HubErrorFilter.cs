using BasicApi.Middleware.Exceptions;
using Microsoft.AspNetCore.SignalR;

namespace BasicApi.Hubs;

/// <summary>
/// The hub's ExceptionHandlingMiddleware: a domain error becomes a <see cref="HubException"/> with the REST error code;
/// anything else is logged and reaches the client without details.
/// </summary>
public sealed class HubErrorFilter(ILogger<HubErrorFilter> logger) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try
        {
            return await next(invocationContext);
        }
        catch (DomainException ex)
        {
            throw HubErrors.Create(ex.ErrorCode, ex.Message);
        }
        catch (OperationCanceledException) when (invocationContext.Context.ConnectionAborted.IsCancellationRequested)
        {
            throw; // the client left, this is not a server error
        }
        catch (Exception ex) when (ex is not HubException)
        {
            logger.LogError(ex, "Hub method {Method} failed for connection {ConnectionId}",
                invocationContext.HubMethodName, invocationContext.Context.ConnectionId);
            throw;
        }
    }

    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OnConnectedAsync failed for connection {ConnectionId}", context.Context.ConnectionId);
            throw;
        }
    }

    public async Task OnDisconnectedAsync(
        HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        try
        {
            await next(context, exception);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OnDisconnectedAsync failed for connection {ConnectionId}", context.Context.ConnectionId);
            throw;
        }
    }
}
