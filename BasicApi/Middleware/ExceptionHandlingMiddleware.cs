using System.Text.Json;
using System.Text.Json.Serialization;
using BasicApi.Middleware.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace BasicApi.Middleware;

/// <summary>
/// Turns exceptions into ProblemDetails: domain errors keep their code, anything else is a 500 without details
/// outside Development. 5xx are logged with the traceId the client sees in the response.
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
        catch (BadHttpRequestException ex)
        {
            // Kestrel refused the request itself: a body over the limit, or cut short by a client
            // that went away. The client's doing, not a failure.
            var tooLarge = ex.StatusCode == StatusCodes.Status413PayloadTooLarge;
            _logger.LogInformation("{Method} {Path} -> {StatusCode}: {Detail}",
                context.Request.Method, context.Request.Path, ex.StatusCode, ex.Message);

            await WriteProblemDetailsAsync(context, ex.StatusCode,
                tooLarge ? "Payload Too Large" : "Bad Request",
                tooLarge ? "The request is too large." : "The request could not be read.",
                errorCode: tooLarge ? "REQUEST_TOO_LARGE" : "BAD_REQUEST");
        }
        catch (DomainException ex)
        {
            var (status, title) = ex switch
            {
                UnauthorizedException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
                ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden"),
                NotFoundException => (StatusCodes.Status404NotFound, "Not Found"),
                ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
                ServiceUnavailableException => (StatusCodes.Status503ServiceUnavailable, "Service Unavailable"),
                _ => (StatusCodes.Status400BadRequest, "Bad Request")
            };

            _logger.LogInformation("{Method} {Path} -> {StatusCode} {ErrorCode}: {Detail}",
                context.Request.Method, context.Request.Path, status, ex.ErrorCode, ex.Message);

            if (ex is UnauthorizedException)
                SetWwwAuthenticateHeader(context);

            await WriteProblemDetailsAsync(context, status, title, ex.Message, errorCode: ex.ErrorCode,
                userIds: (ex as PrivacyRestrictedException)?.UserIds);
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
        string? stackTrace = null,
        IReadOnlyList<Guid>? userIds = null)
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

        // Whom the refusal is about, when there are several (adding to a group).
        if (userIds is not null)
        {
            problemDetails.Extensions["userIds"] = userIds;
        }

        var json = JsonSerializer.Serialize(problemDetails, JsonOptions);
        await context.Response.WriteAsync(json);
    }

    private static void SetWwwAuthenticateHeader(HttpContext context)
    {
        context.Response.Headers.WWWAuthenticate = "Bearer realm=\"basicapi\", error=\"invalid_token\", error_description=\"Authentication required\"";
    }
}

