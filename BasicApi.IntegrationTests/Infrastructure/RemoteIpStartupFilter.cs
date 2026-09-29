using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace BasicApi.IntegrationTests.Infrastructure;

/// <summary>
/// TestServer не заполняет адрес собеседника. Фильтр ставит его первым в конвейере —
/// так запрос выглядит пришедшим с указанного адреса (например, от прокси).
/// </summary>
public sealed class RemoteIpStartupFilter(IPAddress remoteIp) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            context.Connection.RemoteIpAddress = remoteIp;
            return nextMiddleware(context);
        });
        next(app);
    };
}
