using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using BasicApi.Hubs;
using BasicApi.Middleware.Exceptions;
using BasicApi.Services;
using BasicApi.Services.Events;
using BasicApi.Storage;
using BasicApi.Storage.Interfaces;
using BasicApi.Storage.Migrations;
using BasicApi.Storage.Repositories;
using BasicApi.Storage.Services;
using FluentMigrator.Runner;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BasicApi.Extensions;

public static class ServiceExtensions
{
    public const string ReadyTag = "ready";

    public static IServiceCollection AddApiServices(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
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
                services.AddApiRateLimiting(configuration);
                services.AddSignalR(options =>
                {
                    // Доменные ошибки → HubException с кодом; остальное — в лог.
                    options.AddFilter<HubErrorFilter>();
                    // Разрешаем параллельную обработку вызовов
                    options.MaximumParallelInvocationsPerClient = 2;
                    // Текст исключений клиенту — только при разработке: в проде он
                    // раскрывает внутренности (SQL, пути, имена классов).
                    options.EnableDetailedErrors = environment.IsDevelopment();
                    // Максимальный размер входящего сообщения (128KB для поддержки base64 изображений)
                    options.MaximumReceiveMessageSize = 128 * 1024;
                    // Ограничиваем буфер для команд, чтобы избежать накопления зависших вызовов
                    options.StreamBufferCapacity = 10;
                });


        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection is not configured");

                services.AddSingleton<IDbConnectionFactory>(new NpgsqlConnectionFactory(connectionString));

        // Одна сессия БД на запрос (вызов хаба): репозитории делят её транзакцию.
        services.AddScoped<IDbSession, DbSession>();
        services.AddScoped<IChatRepository, ChatRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();

        // Доменные сервисы: контроллеры и хаб — только адаптеры над ними.
        services.AddScoped<IMembershipService, MembershipService>();
        services.AddScoped<IChatPolicy, ChatPolicy>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<IPresenceService, PresenceService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<AuthService>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<IChatEventPublisher, SignalRChatEventPublisher>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IUserStatusService, UserStatusService>();
        services.AddSingleton<HubConnectionRegistry>();

        // JWT
        services.AddScoped<IJwtService, JwtService>();

        // За обратным прокси адрес соединения — это адрес прокси. Настоящий IP
        // клиента (для лимитов и сессий) берём из X-Forwarded-For, но только если
        // запрос пришёл из доверенной сети прокси: иначе заголовок подделает кто угодно.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var network in (configuration["ReverseProxy:TrustedNetworks"] ?? string.Empty)
                         .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });

        // Health checks: /health/live — процесс жив; /health/ready — ещё и база доступна.
        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgres", tags: [ReadyTag], timeout: TimeSpan.FromSeconds(3));

        // FluentMigrator
        services.AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddPostgres()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(InitialCreate).Assembly).For.Migrations());

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

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        // Реальный клиент при открытии и листании чатов делает десятки запросов в минуту,
        // поэтому лимит на пользователя щедрый. Анонимам (вход, регистрация, запросы без
        // токена) — отдельный, по IP. Лимит только по IP не годится: вся команда за
        // офисным NAT делила бы один бюджет.
        var perUser = configuration.GetValue("RateLimiting:PerUserPerMinute", 300);
        var perIp = configuration.GetValue("RateLimiting:PerIpPerMinute", 100);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var userId = context.User.Identity?.IsAuthenticated == true
                    ? context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? context.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                    : null;

                return userId is not null
                    ? RateLimitPartition.GetSlidingWindowLimiter("user:" + userId, _ => PerMinute(perUser))
                    : RateLimitPartition.GetSlidingWindowLimiter(
                        "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                        _ => PerMinute(perIp));
            });

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

    /// <summary>Скользящее окно в минуту из 6 сегментов: без двойного всплеска на стыке окон.</summary>
    private static SlidingWindowRateLimiterOptions PerMinute(int permits) => new()
    {
        PermitLimit = permits,
        Window = TimeSpan.FromMinutes(1),
        SegmentsPerWindow = 6,
        QueueLimit = 0
    };

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
