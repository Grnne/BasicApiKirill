using BasicApi.Extensions;
using BasicApi.Hubs;
using BasicApi.Middleware;
using FluentMigrator.Runner;

namespace BasicApi;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddApiServices(builder.Configuration);

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
        app.UseDefaultFiles();
        app.UseStaticFiles();

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
            context.Response.Redirect("/app/index.html");
            return Task.CompletedTask;
        });

        app.Run();
    }
}