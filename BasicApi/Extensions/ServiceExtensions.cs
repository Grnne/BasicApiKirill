using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using BasicApi.Hubs;
using BasicApi.Middleware.Exceptions;
using BasicApi.Services;
using BasicApi.Services.Events;
using BasicApi.Services.Media;
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

    /// <summary>Limit on commands (send, "typing") — same as the hub's per-connection limit, but per user.</summary>
    public const string CommandsRateLimitPolicy = "commands";

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
            // Domain errors → HubException with a code; anything else goes to the log.
            options.AddFilter<HubErrorFilter>();
            // Allow parallel handling of calls
            options.MaximumParallelInvocationsPerClient = 2;
            // Exception text goes to the client only in development: in production it
            // reveals internals (SQL, paths, class names).
            options.EnableDetailedErrors = environment.IsDevelopment();
            // Maximum size of an incoming message. Commands are moving to REST
            // (POST /api/chats/{id}/messages, /typing); once the front end moves over, the hub
            // will only need a few KB — then lower this.
            options.MaximumReceiveMessageSize = 128 * 1024;
            // Limit the buffer for commands to avoid piling up hung calls
            options.StreamBufferCapacity = 10;
        });

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection is not configured");

        services.AddSingleton<IDbConnectionFactory>(new NpgsqlConnectionFactory(connectionString));

        // One DB session per request (hub call): repositories share its transaction.
        services.AddScoped<IDbSession, DbSession>();
        services.AddScoped<IChatRepository, ChatRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<IUpdateJournal, UpdateJournalRepository>();
        services.AddScoped<IReactionRepository, ReactionRepository>();
        services.AddScoped<IDraftRepository, DraftRepository>();
        services.AddScoped<IGroupRepository, GroupRepository>();
        services.AddScoped<IAttachmentRepository, AttachmentRepository>();

        // Domain services: controllers and the hub are only adapters over them.
        services.AddScoped<IMembershipService, MembershipService>();
        services.Configure<MessageOptions>(configuration.GetSection(MessageOptions.Section));
        services.Configure<GroupOptions>(configuration.GetSection(GroupOptions.Section));
        services.AddScoped<IChatPolicy, ChatPolicy>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IMessageService, MessageService>();
        services.AddScoped<IReactionService, ReactionService>();
        services.AddScoped<IReadStateService, ReadStateService>();
        services.AddScoped<IDraftService, DraftService>();
        services.AddScoped<IGroupService, GroupService>();
        // Files: kept in S3-compatible storage; without Storage:Endpoint media is off (503).
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.Section));
        services.Configure<MediaOptions>(configuration.GetSection(MediaOptions.Section));
        services.AddSingleton<IObjectStorage, S3ObjectStorage>();
        services.AddScoped<IMediaService, MediaService>();
        services.AddSingleton<MediaCleanup>();
        services.AddHostedService(sp => sp.GetRequiredService<MediaCleanup>());
        services.AddScoped<IPresenceService, PresenceService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<ISyncService, SyncService>();
        services.AddScoped<AuthService>();
        services.AddScoped<ISessionService, SessionService>();
        // Events: messages and new chats go through the outbox in the transaction of the change,
        // "typing" and online go out immediately (ephemeral).
        services.AddScoped<SignalRChatEventPublisher>();
        services.AddScoped<IChatEventPublisher, OutboxChatEventPublisher>();
        services.AddSingleton<OutboxSignal>();
        services.AddSingleton<OutboxDispatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<OutboxDispatcher>());
        services.AddSingleton<JournalCleanup>();
        services.AddHostedService(sp => sp.GetRequiredService<JournalCleanup>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IUserStatusService, UserStatusService>();
        services.AddSingleton<HubConnectionRegistry>();
        services.AddHostedService<HubSessionMonitor>();

        // JWT
        services.AddScoped<IJwtService, JwtService>();

        // Behind a reverse proxy the connection address is the proxy's address. The real
        // client IP (for limits and sessions) is taken from X-Forwarded-For, but only if the
        // request came from a trusted proxy network: otherwise anyone could forge the header.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var network in (configuration["ReverseProxy:TrustedNetworks"] ?? string.Empty)
                         .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });

        // Health checks: /health/live — the process is alive; /health/ready — the database is reachable too.
        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgres", tags: [ReadyTag], timeout: TimeSpan.FromSeconds(3));

        // FluentMigrator
        services.AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddPostgres()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(InitialCreate).Assembly).For.Migrations());

        // CORS — only explicitly allowed origins (wildcard + AllowCredentials
        // would mean any site could make requests on behalf of the user).
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

                // SignalR sends the token in the query string: WebSocket cannot carry headers
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
                        // Suppress the default empty 401 from .NET and throw instead,
                        // so ExceptionHandlingMiddleware answers with ProblemDetails
                        context.HandleResponse();

                        throw new UnauthorizedException("Authentication required", "TOKEN_MISSING_OR_EXPIRED");
                    },
                    OnForbidden = context =>
                    {
                        // Suppress the default empty 403 from .NET and throw instead,
                        // so ExceptionHandlingMiddleware answers with ProblemDetails
                        throw new ForbiddenException("Access denied", "ACCESS_DENIED");
                    }
                };
            });

        services.AddAuthorization();
        return services;
    }

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        // A real client makes dozens of requests per minute when opening and scrolling chats,
        // so the per-user limit is generous. Anonymous callers (login, registration, requests without a
        // token) get a separate per-IP limit. A per-IP-only limit will not do: a whole team behind
        // an office NAT would share a single budget.
        var perUser = configuration.GetValue("RateLimiting:PerUserPerMinute", 300);
        var perIp = configuration.GetValue("RateLimiting:PerIpPerMinute", 100);
        var commandsPer10Seconds = configuration.GetValue("RateLimiting:CommandsPer10Seconds", 20);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var userId = RateLimitUserId(context);

                return userId is not null
                    ? RateLimitPartition.GetSlidingWindowLimiter("user:" + userId, _ => PerMinute(perUser))
                    : RateLimitPartition.GetSlidingWindowLimiter(
                        "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                        _ => PerMinute(perIp));
            });

            // Brute-force protection: 5 login/registration attempts per minute from one IP
            // (applies IN ADDITION to the global limit, both must pass).
            options.AddPolicy("auth", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            // Sending and "typing" write to the database and fan out to all participants — a separate
            // per-user limit (in addition to the general one), like the hub's per-connection one.
            options.AddPolicy(CommandsRateLimitPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: RateLimitUserId(context) is { } userId
                        ? "user:" + userId
                        : "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = commandsPer10Seconds,
                        Window = TimeSpan.FromSeconds(10),
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

    private static string? RateLimitUserId(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true
            ? context.User.FindFirstValue(ClaimTypes.NameIdentifier)
              ?? context.User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            : null;

    /// <summary>Sliding window of one minute in 6 segments: no double burst at the window boundary.</summary>
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
