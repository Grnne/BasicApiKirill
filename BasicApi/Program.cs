using BasicApi.Extensions;
using BasicApi.Hubs;
using BasicApi.Middleware;
using FluentMigrator.Runner;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Net.Http.Headers;
using System.IO.Compression;

namespace BasicApi;

public class Program
{
    /// <summary>
    /// CSP заголовком, а не только &lt;meta&gt; в index.html: frame-ancestors в meta
    /// не работает, а заголовок покрывает и страницы, которые отдаёт сам сервер.
    /// connect-src 'self' — даже при XSS скрипт не отправит токен на чужой хост
    /// (same-origin ws/wss 'self' тоже покрывает). 'unsafe-inline' только для
    /// стилей: Vue вставляет их тегом &lt;style&gt;; для скриптов послаблений нет.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; font-src 'self'; connect-src 'self'; " +
        "frame-ancestors 'none'; base-uri 'none'; object-src 'none'; form-action 'self'";

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Падаем до старта, если конфиг опасен или неполон (дефолтный JWT-ключ и т.п.).
        ConfigurationValidation.Validate(builder.Configuration, builder.Environment);

        builder.Services.AddApiServices(builder.Configuration, builder.Environment);

        // SignalR пишет Error на любое исключение метода хаба — и на ожидаемые ошибки
        // клиента (NOT_A_MEMBER, MESSAGE_EMPTY), для которых REST пишет Information.
        // Настоящие ошибки хаба пишет HubErrorFilter — с именем метода и уровнем Error.
        builder.Logging.AddFilter("Microsoft.AspNetCore.SignalR.Internal.DefaultHubDispatcher", LogLevel.None);

        // Сжатие статики фронтенда: бандл ужимается втрое.
        // Пока раздачей занимается Kestrel, это его работа; появится nginx —
        // сжатие переедет туда.
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

        // Настоящий IP и схема клиента из заголовков прокси — раньше всего остального,
        // чтобы логи, лимиты и сессии видели клиента, а не Caddy.
        app.UseForwardedHeaders();

        // Global error handling — first after forwarded headers
        app.UseMiddleware<ExceptionHandlingMiddleware>();

        // Run migrations
        using (var scope = app.Services.CreateScope())
        {
            scope.ServiceProvider
                .GetRequiredService<IMigrationRunner>()
                .MigrateUp();
        }
        // Заголовки безопасности для всех ответов. Дёшево и закрывает
        // несколько типовых атак: подмену типа файла, вставку страницы
        // в чужой iframe, утечку адреса через Referer и — через CSP —
        // большую часть последствий XSS.
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.ContentSecurityPolicy = ContentSecurityPolicy;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            await next();
        });

        app.UseResponseCompression();

        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = context =>
            {
                var path = context.Context.Request.Path.Value ?? string.Empty;

                // В именах файлов сборки есть хеш содержимого: меняется файл —
                // меняется имя. Значит их можно кэшировать навсегда.
                if (path.StartsWith("/client/assets/", StringComparison.OrdinalIgnoreCase))
                {
                    context.Context.Response.Headers[HeaderNames.CacheControl] =
                        "public,max-age=31536000,immutable";
                }
                // А вот index.html обязан проверяться каждый раз — иначе
                // пользователь останется на старой версии приложения.
                else if (path.EndsWith("index.html", StringComparison.OrdinalIgnoreCase))
                {
                    context.Context.Response.Headers[HeaderNames.CacheControl] = "no-cache";
                }
            }
        });

        // CORS
        app.UseCors("Default");

        // Swagger по умолчанию только в Development; в проде — флагом Swagger:Enabled.
        if (app.Configuration.GetValue("Swagger:Enabled", false))
            app.UseSwaggerWithUI();

        // TLS завершается на обратном прокси, поэтому HTTPS-редиректа здесь нет.
        // Порядок: Authentication → RateLimiter → Authorization.
        // Лимитеру нужен уже известный пользователь (лимит по userId), и он должен
        // стоять до авторизации — иначе поток запросов без токена обрывается на 401
        // раньше лимитера и не ограничивается вовсе.
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();

        // Соединение живёт, пока жив вход, с которого оно открыто, а не access-токен:
        // веб-клиент при переподключении отдаёт тот же токен и после закрытия по его
        // истечению оставался без событий. Конец входа обрывает соединения сразу
        // (logout, logout-all) или в течение минуты (HubSessionMonitor).
        app.MapHub<ChatHub>("/hubs/chat");
        app.MapControllers();

        // Проверки для compose и балансировщика: без авторизации и вне rate limit,
        // иначе частые проверки съедят лимит и получат 429.
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
            .DisableRateLimiting();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ServiceExtensions.ReadyTag)
        }).DisableRateLimiting();

        app.MapGet("/signalr-docs", async context =>
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            var html = await File.ReadAllTextAsync(
                Path.Combine(app.Environment.WebRootPath, "signalr-docs.html"));
            await context.Response.WriteAsync(html);
        });

        app.MapGet("/", context =>
        {
            context.Response.Redirect("/client/");
            return Task.CompletedTask;
        });

        // Клиентский роутинг: /client/chat — это маршрут внутри SPA, файла с
        // таким именем нет. Ограничение "/client/{*path:nonfile}" важно:
        // без него опечатка в адресе /api/... возвращала бы HTML вместо 404.
        app.MapFallbackToFile("/client/{*path:nonfile}", "client/index.html");

        app.Run();
    }
}