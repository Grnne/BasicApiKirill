using BasicApi.Extensions;
using BasicApi.Hubs;
using FluentMigrator.Runner;

namespace BasicApi;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddApiServices(builder.Configuration);

        // Добавить CORS для SignalR
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", policy =>
            {
                policy.AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials()
                      .SetIsOriginAllowed(_ => true); // для разработки
            });
        });

        var app = builder.Build();

        // Миграции при старте
        using (var scope = app.Services.CreateScope())
        {
            scope.ServiceProvider
                .GetRequiredService<IMigrationRunner>()
                .MigrateUp();
        }

        // Добавить CORS перед маршрутами
        app.UseCors("AllowAll");
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseSwaggerWithUI();
        app.MapHub<ChatHub>("/hubs/chat");

        app.MapGet("/", context =>
        {
            context.Response.Redirect("/swagger");
            return Task.CompletedTask;
        });

        app.UseHttpsRedirection();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();

        app.Run();
    }
}