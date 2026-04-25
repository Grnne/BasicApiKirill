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

        // Run migrations only in development
        if (app.Environment.IsDevelopment())
        {
            using (var scope = app.Services.CreateScope())
            {
                scope.ServiceProvider
                    .GetRequiredService<IMigrationRunner>()
                    .MigrateUp();
            }
        }

        app.UseDefaultFiles();
        app.UseStaticFiles();

        // CORS
        app.UseCors("AllowAll");

        app.UseSwaggerWithUI();

        // Order: HTTPS → Auth → Authorization → endpoints
        app.UseHttpsRedirection();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapHub<ChatHub>("/hubs/chat");
        app.MapControllers();

        app.MapGet("/", context =>
        {
            context.Response.Redirect("/swagger");
            return Task.CompletedTask;
        });

        app.Run();
    }
}