using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using BasicApi.Services.Events;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

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
                Title = swaggerConfig["Title"] ?? "Chat API",
                Version = swaggerConfig["Version"] ?? "v1",
                Description = swaggerConfig["Description"] ?? "Real-time Chat API with JWT authentication",
                Contact = new OpenApiContact
                {
                    Name = swaggerConfig["Contact:Name"] ?? "API Support",
                    Email = swaggerConfig["Contact:Email"] ?? "support@example.com",
                    Url = !string.IsNullOrEmpty(swaggerConfig["Contact:Url"])
                        ? new Uri(swaggerConfig["Contact:Url"]!)
                        : null
                }
            });

            c.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = JwtBearerDefaults.AuthenticationScheme,
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Enter your JWT token. Example: 'eyJhbGci...'"
            });

            c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(JwtBearerDefaults.AuthenticationScheme, document)] = []
            });

            // Nullability as declared in C#: generated clients get string, not string | null, where null never comes.
            c.SupportNonNullableReferenceTypes();
            c.UseAllOfToExtendReferenceSchemas();
            c.DocumentFilter<HubEventSchemasFilter>();
            c.SchemaFilter<OmittedWhenNullSchemaFilter>();

            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
                c.IncludeXmlComments(xmlPath);
        });

        return services;
    }

    public static WebApplication UseSwaggerWithUI(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", $"{app.Environment.ApplicationName} v1");
            c.RoutePrefix = "swagger";
            c.DocumentTitle = "Chat API Documentation";
            c.DefaultModelsExpandDepth(-1);
            c.DisplayRequestDuration();
            c.EnableTryItOutByDefault();
        });

        return app;
    }
}

/// <summary>
/// Adds the payloads of hub events to the document's schemas: SignalR is not described by OpenAPI, but
/// clients generate their types from this document, events included.
/// </summary>
internal sealed class HubEventSchemasFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        var payloads = typeof(IChatEventPublisher).GetMethods()
            .SelectMany(m => m.GetParameters())
            .Select(p => p.ParameterType)
            .Where(t => t.IsClass && t.Namespace?.StartsWith("BasicApi.Models", StringComparison.Ordinal) == true)
            .Distinct();

        foreach (var type in payloads)
            context.SchemaGenerator.GenerateSchema(type, context.SchemaRepository);
    }
}

/// <summary>
/// Marks properties left out of the JSON when null (<c>x-omitted-when-null</c>): every other property of
/// an answer is always written, so generated clients may treat it as present.
/// </summary>
internal sealed class OmittedWhenNullSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema.Properties is null)
            return;

        foreach (var property in context.Type.GetProperties())
        {
            if (property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition != JsonIgnoreCondition.WhenWritingNull)
                continue;
            if (schema.Properties.TryGetValue(JsonNamingPolicy.CamelCase.ConvertName(property.Name), out var target)
                && target is OpenApiSchema concrete)
            {
                concrete.Extensions ??= new Dictionary<string, IOpenApiExtension>();
                concrete.Extensions["x-omitted-when-null"] = new JsonNodeExtension(JsonValue.Create(true));
            }
        }
    }
}
