using System.Reflection;
using Microsoft.OpenApi;

namespace BasicApi.Extensions;

public static class SwaggerExtensions
{
    public static IServiceCollection AddSwaggerWithDocs(this IServiceCollection services, IConfiguration configuration)
    {
        var swaggerConfig = configuration.GetSection("Swagger");

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = swaggerConfig["Title"] ?? "My Web API",
                Version = swaggerConfig["Version"] ?? "v1",
                Description = swaggerConfig["Description"] ?? "Web API with MVC controllers",
                Contact = new OpenApiContact
                {
                    Name = swaggerConfig["Contact:Name"] ?? "API Support",
                    Email = swaggerConfig["Contact:Email"] ?? "support@example.com",
                    Url = !string.IsNullOrEmpty(swaggerConfig["Contact:Url"])
                        ? new Uri(swaggerConfig["Contact:Url"])
                        : null
                }
            });

            // Include XML comments if documentation file exists
            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                c.IncludeXmlComments(xmlPath);
            }
        });

        return services;
    }

    public static WebApplication UseSwaggerWithUI(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", $"{app.Environment.ApplicationName} v1");
                c.RoutePrefix = "swagger";

                // Customize Swagger UI
                c.DocumentTitle = "API Documentation";
                c.DefaultModelsExpandDepth(-1);
                c.DisplayRequestDuration();
                c.EnableTryItOutByDefault();
            });
        }

        return app;
    }
}