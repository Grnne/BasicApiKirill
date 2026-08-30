using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using BasicApi.Features.Auth;
using BasicApi.Features.Chats;
using BasicApi.Features.Users;
using BasicApi.Middleware.Exceptions;
using BasicApi.Services;
using BasicApi.Storage.Interfaces;
using BasicApi.Storage.Migrations;
using BasicApi.Storage.Repositories;
using BasicApi.Storage.Services;
using FluentMigrator.Runner;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

namespace BasicApi.Extensions;

public static class ServiceExtensions
{
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
                                services.AddControllers()
            .ConfigureApiBehaviorOptions(options =>
        {
                        options.InvalidModelStateResponseFactory = context =>
            {
                var errors = context.ModelState
                    .Where(e => e.Value?.Errors.Count > 0)
                    .ToDictionary(
                        e => e.Key,
                        e => e.Value!.Errors.Select(x => new
                        {
                            code = GetValidationErrorCode(x.ErrorMessage),
                            message = x.ErrorMessage
                        }).ToArray()
                    );

                var problemDetails = new ProblemDetails
                {
                    Type = "about:blank",
                    Title = "Bad Request",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = "One or more validation errors occurred.",
                    Instance = context.HttpContext.Request.Path,
                    Extensions =
                    {
                        ["traceId"] = context.HttpContext.TraceIdentifier,
                        ["errorCode"] = "VALIDATION_ERROR",
                        ["errors"] = errors
                    }
                };

                return new ObjectResult(problemDetails)
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    ContentTypes = { "application/problem+json" }
                };
            };
        });
                services.AddSwaggerWithDocs(configuration);
                services.AddJwtAuth(configuration);
                services.AddApiRateLimiting();
                services.AddSignalR(options =>
                {
                    // Разрешаем параллельную обработку вызовов
                    options.MaximumParallelInvocationsPerClient = 2;
                    // EnableDetailedErrors — помогает понять что падает при разработке
                    options.EnableDetailedErrors = true;
                    // Максимальный размер входящего сообщения (128KB для поддержки base64 изображений)
                    options.MaximumReceiveMessageSize = 128 * 1024;
                    // Ограничиваем буфер для команд, чтобы избежать накопления зависших вызовов
                    options.StreamBufferCapacity = 10;
                });


        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection is not configured");

                services.AddSingleton<IDbConnectionFactory>(new NpgsqlConnectionFactory(connectionString));

        services.AddScoped<IChatRepository, ChatRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddSingleton<IUserStatusService, UserStatusService>();
        services.AddScoped<AuthHandler>();
        services.AddScoped<ChatsHandler>();
        services.AddScoped<UsersHandler>();

        // JWT
        services.AddScoped<IJwtService, JwtService>();

        // FluentMigrator
        services.AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddPostgres()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(InitialCreate).Assembly).For.Migrations())
            .AddLogging(lb => lb.AddConsole());

        // CORS — только явно разрешённые origin'ы (wildcard + AllowCredentials
        // означал бы, что любой сайт может делать запросы от имени пользователя).
        var allowedOrigins = (configuration["Cors:AllowedOrigins"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        services.AddCors(options =>
        {
            options.AddPolicy("Default", policy =>
            {
                policy.AllowAnyHeader()
                      .AllowAnyMethod();

                if (allowedOrigins.Length > 0)
                    policy.WithOrigins(allowedOrigins).AllowCredentials();
            });
        });

        return services;
    }

    public static IServiceCollection AddJwtAuth(this IServiceCollection services, IConfiguration config)
    {
        var key = config["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is not configured");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                    ValidateIssuer = true,
                    ValidIssuer = config["Jwt:Issuer"],
                    ValidateAudience = true,
                    ValidAudience = config["Jwt:Audience"],
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };

                                // SignalR ������� ����� ����� query string
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;

                        if (!string.IsNullOrEmpty(accessToken) &&
                            path.StartsWithSegments("/hubs/chat"))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    },
                    OnChallenge = context =>
                    {
                        // ��������� ����������� ������ 401 ����� �� .NET
                        // � ������ ����������, ������� ������� ExceptionHandlingMiddleware
                        context.HandleResponse();

                        throw new UnauthorizedException("Authentication required", "TOKEN_MISSING_OR_EXPIRED");
                    },
                    OnForbidden = context =>
                    {
                        // ��������� ����������� ������ 403 ����� �� .NET
                        // � ������ ����������, ������� ������� ExceptionHandlingMiddleware
                        throw new ForbiddenException("Access denied", "ACCESS_DENIED");
                    }
                };
            });

        services.AddAuthorization();
        return services;
    }

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Защита от перегрузки: базовый лимит на весь REST API с одного IP,
            // чтобы никто не мог заDDoS'ить сервер потоком обычных запросов.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            // Брутфорс-защита: 5 попыток логина/регистрации в минуту с одного IP
            // (действует ДОПОЛНИТЕЛЬНО к глобальному лимиту, оба должны пройти).
            options.AddPolicy("auth", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.ContentType = "application/problem+json";

                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var ra)
                    ? (int)ra.TotalSeconds
                    : 60;
                context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString();

                var problemDetails = new ProblemDetails
                {
                    Type = "about:blank",
                    Title = "Too Many Requests",
                    Status = StatusCodes.Status429TooManyRequests,
                    Detail = "Too many attempts. Please try again later.",
                    Instance = context.HttpContext.Request.Path,
                    Extensions =
                    {
                        ["traceId"] = context.HttpContext.TraceIdentifier,
                        ["errorCode"] = "RATE_LIMITED"
                    }
                };

                await context.HttpContext.Response.WriteAsync(
                    JsonSerializer.Serialize(problemDetails, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    }),
                    cancellationToken);
            };
        });

        return services;
    }

    /// <summary>
    /// Maps ASP.NET default validation error messages to machine-readable codes.
    /// This allows clients to handle validation errors programmatically without parsing human text.
    /// </summary>
    private static string GetValidationErrorCode(string errorMessage)
    {
        if (errorMessage.Contains("required", StringComparison.OrdinalIgnoreCase) ||
            errorMessage.Contains("must be provided", StringComparison.OrdinalIgnoreCase))
            return "REQUIRED";

        if (errorMessage.Contains("maximum length", StringComparison.OrdinalIgnoreCase) ||
            errorMessage.Contains("max length", StringComparison.OrdinalIgnoreCase) ||
            errorMessage.Contains("too long", StringComparison.OrdinalIgnoreCase))
            return "MAX_LENGTH";

        if (errorMessage.Contains("minimum length", StringComparison.OrdinalIgnoreCase) ||
            errorMessage.Contains("min length", StringComparison.OrdinalIgnoreCase) ||
            errorMessage.Contains("at least", StringComparison.OrdinalIgnoreCase))
            return "MIN_LENGTH";

        if (errorMessage.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
            errorMessage.Contains("not valid", StringComparison.OrdinalIgnoreCase) ||
            errorMessage.Contains("a valid", StringComparison.OrdinalIgnoreCase))
            return "INVALID_FORMAT";

        if (errorMessage.Contains("range", StringComparison.OrdinalIgnoreCase) ||
            errorMessage.Contains("between", StringComparison.OrdinalIgnoreCase))
            return "OUT_OF_RANGE";

        if (errorMessage.Contains("match", StringComparison.OrdinalIgnoreCase) ||
            errorMessage.Contains("must match", StringComparison.OrdinalIgnoreCase) ||
            errorMessage.Contains("do not match", StringComparison.OrdinalIgnoreCase))
            return "MISMATCH";

        return "VALIDATION_ERROR";
    }
}
