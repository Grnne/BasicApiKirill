using System.Text;
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
        services.AddSignalR();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection is not configured");

                services.AddSingleton<IDbConnectionFactory>(new NpgsqlConnectionFactory(connectionString));

        services.AddScoped<IChatRepository, ChatRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IChatService, ChatService>();
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

        //�������
        services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", policy =>
            {
                policy.AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials()
                      .SetIsOriginAllowed(_ => true);
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
