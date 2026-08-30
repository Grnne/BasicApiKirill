using BasicApi.Extensions;
using BasicApi.Hubs;
using BasicApi.Middleware;
using FluentMigrator.Runner;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Net.Http.Headers;
using System.IO.Compression;

namespace BasicApi;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddApiServices(builder.Configuration);

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

        // Global error handling — MUST be first middleware
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
        // в чужой iframe и утечку адреса через Referer.
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
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

        app.UseSwaggerWithUI();

        // Order: HTTPS → Auth → Authorization → endpoints
        app.UseHttpsRedirection();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

                app.MapHub<ChatHub>("/hubs/chat");
        app.MapControllers();

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