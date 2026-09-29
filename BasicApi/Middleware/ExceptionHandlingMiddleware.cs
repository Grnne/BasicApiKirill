using System.Text.Json;
using System.Text.Json.Serialization;
using BasicApi.Middleware.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace BasicApi.Middleware;

/// <summary>
/// Global exception handling middleware.
/// Catches all unhandled exceptions and returns structured ProblemDetails responses.
/// Maps known exception types to appropriate HTTP status codes.
/// Strips internal details in production (non-development) environments.
///
/// Logging: 5xx — Error with the exception and traceId (the same traceId the client
/// sees in the response, so a user report can be matched to the log line);
/// domain 4xx — Information without a stack trace; client aborts — Debug.
/// </summary>
public class ExceptionHandlingMiddleware
{
    /// <summary>Nginx convention for "client closed request"; never reaches the client.</summary>
    public const int StatusClientClosedRequest = 499;

    private readonly RequestDelegate _next;
    private readonly IHostEnvironment _env;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ExceptionHandlingMiddleware(
        RequestDelegate next, IHostEnvironment env, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _env = env;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client left on its own (closed the tab, network dropped) — nobody to respond to.
            _logger.LogDebug("Request aborted by client: {Method} {Path}",
                context.Request.Method, context.Request.Path);

            if (!context.Response.HasStarted)
                context.Response.StatusCode = StatusClientClosedRequest;
        }
        catch (Exception ex) when (context.Response.HasStarted)
        {
            // Headers are already sent: ProblemDetails cannot be written. We log
            // and rethrow — Kestrel will drop the connection, the client will see the drop.
            _logger.LogError(ex,
                "Unhandled exception after response started: {Method} {Path}, traceId={TraceId}",
                context.Request.Method, context.Request.Path, context.TraceIdentifier);
            throw;
        }
        catch (DomainException ex)
        {
            var (status, title) = ex switch
            {
                UnauthorizedException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
                ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden"),
                NotFoundException => (StatusCodes.Status404NotFound, "Not Found"),
                ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
                _ => (StatusCodes.Status400BadRequest, "Bad Request")
            };

            _logger.LogInformation("{Method} {Path} -> {StatusCode} {ErrorCode}: {Detail}",
                context.Request.Method, context.Request.Path, status, ex.ErrorCode, ex.Message);

            if (ex is UnauthorizedException)
                SetWwwAuthenticateHeader(context);

            await WriteProblemDetailsAsync(context, status, title, ex.Message, errorCode: ex.ErrorCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception: {Method} {Path}, traceId={TraceId}",
                context.Request.Method, context.Request.Path, context.TraceIdentifier);

            await WriteProblemDetailsAsync(context, StatusCodes.Status500InternalServerError,
                "Internal Server Error",
                _env.IsDevelopment() ? ex.Message : "An unexpected error occurred.",
                errorCode: "INTERNAL_ERROR",
                stackTrace: _env.IsDevelopment() ? ex.StackTrace : null);
        }
    }

    private async Task WriteProblemDetailsAsync(
        HttpContext context,
        int statusCode,
        string title,
        string detail,
        string? errorCode = null,
        string? stackTrace = null)
    {
        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = statusCode;

        var problemDetails = new ProblemDetails
        {
            Type = "about:blank",
            Title = title,
            Status = statusCode,
            Detail = detail,
            Instance = context.Request.Path,
            Extensions =
            {
                ["traceId"] = context.TraceIdentifier
            }
        };

        if (errorCode is not null)
        {
            problemDetails.Extensions["errorCode"] = errorCode;
        }

        if (stackTrace is not null)
        {
            problemDetails.Extensions["stackTrace"] = stackTrace;
        }

        var json = JsonSerializer.Serialize(problemDetails, JsonOptions);
        await context.Response.WriteAsync(json);
    }

    private static void SetWwwAuthenticateHeader(HttpContext context)
    {
        context.Response.Headers.WWWAuthenticate = "Bearer realm=\"basicapi\", error=\"invalid_token\", error_description=\"Authentication required\"";
    }
}

