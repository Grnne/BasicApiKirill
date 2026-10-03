using System.IO.Compression;
using BasicApi.Extensions;
using BasicApi.Features.Media;
using BasicApi.Hubs;
using BasicApi.Middleware;
using FluentMigrator.Runner;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Net.Http.Headers;

namespace BasicApi;

public class Program
{
    /// <summary>
    /// CSP as a header, not only &lt;meta&gt; in index.html: frame-ancestors does not work
    /// in meta, and a header also covers the pages served by the server itself.
    /// blob: for pictures and media only: previews of files chosen for upload, made by the page itself.
    /// connect-src 'self': even with XSS a script cannot send the token to a foreign host
    /// (same-origin ws/wss is covered by 'self' too). 'unsafe-inline' is for styles only:
    /// Vue inserts them with a &lt;style&gt; tag; there are no relaxations for scripts.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob:; media-src 'self' blob:; font-src 'self'; connect-src 'self'; " +
        "worker-src 'self'; frame-ancestors 'none'; base-uri 'none'; object-src 'none'; form-action 'self'";

    /// <summary>
    /// Cache-Control for a file of the web client. Build assets carry a content hash in the name, so
    /// they are cached for good; index.html and the service worker keep their names, so they are
    /// revalidated every time — otherwise users would stay on an old version. Null — the default.
    /// </summary>
    public static string? ClientCacheControl(string path)
    {
        if (path.StartsWith("/client/assets/", StringComparison.OrdinalIgnoreCase))
            return "public,max-age=31536000,immutable";
        if (path.EndsWith("index.html", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/client/sw.js", StringComparison.OrdinalIgnoreCase))
            return "no-cache";
        return null;
    }

    /// <summary>
    /// The policy with the file storage allowed for pictures, media and uploads: clients load
    /// files straight from it. In production the proxy serves the storage from the site itself, so
    /// this adds nothing 'self' does not already allow; in local development it is another port.
    /// </summary>
    public static string ContentSecurityPolicyWith(string? storageOrigin) =>
        storageOrigin is null
            ? ContentSecurityPolicy
            : ContentSecurityPolicy
                .Replace("img-src 'self' data: blob:", $"img-src 'self' data: blob: {storageOrigin}")
                .Replace("media-src 'self' blob:", $"media-src 'self' blob: {storageOrigin}")
                .Replace("connect-src 'self'", $"connect-src 'self' {storageOrigin}");

    public static void Main(string[] args)
    {
        // A key pair for push notifications, in the form .env.prod takes it; nothing else starts.
        if (args.Contains("--generate-vapid-keys"))
        {
            var (publicKey, privateKey) = Features.Push.VapidKeys.Generate();
            Console.WriteLine($"PUSH_VAPID_PUBLIC_KEY={publicKey}");
            Console.WriteLine($"PUSH_VAPID_PRIVATE_KEY={privateKey}");
            return;
        }

        var builder = WebApplication.CreateBuilder(args);

        // Fail before startup if the config is dangerous or incomplete (default JWT key and the like).
        ConfigurationValidation.Validate(builder.Configuration, builder.Environment);

        builder.Services.AddApiServices(builder.Configuration, builder.Environment);

        // SignalR logs Error for any hub method exception, including expected client
        // errors (NOT_A_MEMBER, MESSAGE_EMPTY), for which REST logs Information.
        // Real hub errors are logged by HubErrorFilter, with the method name and level Error.
        builder.Logging.AddFilter("Microsoft.AspNetCore.SignalR.Internal.DefaultHubDispatcher", LogLevel.None);

        // Compression of frontend static files: the bundle shrinks threefold.
        builder.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
                ["application/javascript", "text/css", "application/json"]);
        });
        builder.Services.Configure<BrotliCompressionProviderOptions>(
            o => o.Level = CompressionLevel.Fastest);
        builder.Services.Configure<GzipCompressionProviderOptions>(
            o => o.Level = CompressionLevel.Fastest);

        var app = builder.Build();

        // The administrator's password reset, against the configured database; nothing else starts.
        if (Features.Auth.ResetPasswordCommand.IsRequested(args))
        {
            Environment.ExitCode = Features.Auth.ResetPasswordCommand
                .RunAsync(app.Services, args, Console.Out, Console.Error).GetAwaiter().GetResult();
            return;
        }

        // The real client IP and scheme from proxy headers come before everything else,
        // so that logs, limits and sessions see the client, not Caddy.
        app.UseForwardedHeaders();

        app.UseMiddleware<ExceptionHandlingMiddleware>();

        using (var scope = app.Services.CreateScope())
        {
            scope.ServiceProvider
                .GetRequiredService<IMigrationRunner>()
                .MigrateUp();
        }
        // Security headers for all responses. Cheap, and they close
        // several typical attacks: content type spoofing, embedding the page
        // in a foreign iframe, address leakage via Referer and, through CSP,
        // most of the consequences of XSS.
        var storage = app.Configuration.GetSection(StorageOptions.Section)
            .Get<StorageOptions>();
        var contentSecurityPolicy = ContentSecurityPolicyWith(storage?.PublicOrigin);
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy = contentSecurityPolicy;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            await next();
        });

        app.UseResponseCompression();

        // Also for the fallback: the client's routes (/client/chat) are index.html under another address.
        var clientFiles = new StaticFileOptions
        {
            OnPrepareResponse = context =>
            {
                var cacheControl = context.File.Name == "index.html"
                    ? "no-cache"
                    : ClientCacheControl(context.Context.Request.Path.Value ?? string.Empty);
                if (cacheControl is not null)
                    context.Context.Response.Headers[HeaderNames.CacheControl] = cacheControl;
            }
        };
        app.UseDefaultFiles();
        app.UseStaticFiles(clientFiles);

        app.UseCors("Default");

        // Swagger is on by default only in Development; in production, with the Swagger:Enabled flag.
        if (app.Configuration.GetValue("Swagger:Enabled", false))
            app.UseSwaggerWithUI();

        // TLS terminates at the reverse proxy, so there is no HTTPS redirect here.
        // Order: Authentication -> RateLimiter -> Authorization.
        // The limiter needs the user to be already known (limit by userId), and it must
        // come before authorization: otherwise a flood of requests without a token ends in 401
        // before reaching the limiter and is not limited at all.
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();

        // The connection lives as long as the sign-in it was opened from, not the access token:
        // on reconnect the web client sends the same token, so closing on its expiry would
        // leave it without events. The end of a sign-in drops connections immediately
        // (logout, logout-all) or within a minute (HubSessionMonitor).
        app.MapHub<ChatHub>("/hubs/chat");
        app.MapControllers();

        // Checks for compose and the load balancer: no authorization and outside the rate limit,
        // otherwise frequent checks would eat the limit and get 429.
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
            .DisableRateLimiting();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ServiceExtensions.ReadyTag)
        }).DisableRateLimiting();

        // The hub's description goes with the API docs: off in production unless asked for.
        if (app.Configuration.GetValue("Swagger:Enabled", false))
        {
            app.MapGet("/signalr-docs", async context =>
            {
                context.Response.ContentType = "text/html; charset=utf-8";
                var html = await File.ReadAllTextAsync(
                    Path.Combine(app.Environment.WebRootPath, "signalr-docs.html"));
                await context.Response.WriteAsync(html);
            });
        }

        app.MapGet("/", context =>
        {
            context.Response.Redirect("/client/");
            return Task.CompletedTask;
        });

        // Client-side routing: /client/chat is a route inside the SPA, there is no file
        // with such a name. The "/client/{*path:nonfile}" constraint matters:
        // without it a typo in an /api/... address would return HTML instead of 404.
        app.MapFallbackToFile("/client/{*path:nonfile}", "client/index.html", clientFiles);

        app.Run();
    }
}