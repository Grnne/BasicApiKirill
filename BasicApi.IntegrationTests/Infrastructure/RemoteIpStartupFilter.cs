using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace BasicApi.IntegrationTests.Infrastructure;

/// <summary>
/// TestServer does not populate the remote address. The filter puts it first in the pipeline,
/// so the request looks like it came from the given address (for example, from a proxy).
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
