using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace BasicApi.Middleware;

/// <summary>
/// Global exception handling middleware.
/// Catches all unhandled exceptions and returns structured ProblemDetails responses.
/// Maps known exception types to appropriate HTTP status codes.
/// Strips internal details in production (non-development) environments.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IHostEnvironment _env;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ExceptionHandlingMiddleware(RequestDelegate next, IHostEnvironment env)
    {
        _next = next;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (NotFoundException ex)
        {
            await WriteProblemDetailsAsync(context, StatusCodes.Status404NotFound,
                "Not Found", ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            await WriteProblemDetailsAsync(context, StatusCodes.Status403Forbidden,
                "Forbidden", ex.Message);
        }
        catch (BadRequestException ex)
        {
            await WriteProblemDetailsAsync(context, StatusCodes.Status400BadRequest,
                "Bad Request", ex.Message);
        }
        catch (ConflictException ex)
        {
            await WriteProblemDetailsAsync(context, StatusCodes.Status409Conflict,
                "Conflict", ex.Message);
        }
        catch (Exception ex)
        {
            await WriteProblemDetailsAsync(context, StatusCodes.Status500InternalServerError,
                "Internal Server Error",
                _env.IsDevelopment() ? ex.Message : "An unexpected error occurred.",
                _env.IsDevelopment() ? ex.StackTrace : null);
        }
    }

    private async Task WriteProblemDetailsAsync(
        HttpContext context,
        int statusCode,
        string title,
        string detail,
        string? stackTrace = null)
    {
        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = statusCode;

        var problemDetails = new ProblemDetails
        {
            Type = GetProblemTypeUri(statusCode),
            Title = title,
            Status = statusCode,
            Detail = detail,
            Instance = context.Request.Path,
            Extensions =
            {
                ["traceId"] = context.TraceIdentifier
            }
        };

        if (stackTrace is not null)
        {
            problemDetails.Extensions["stackTrace"] = stackTrace;
        }

        var json = JsonSerializer.Serialize(problemDetails, JsonOptions);
        await context.Response.WriteAsync(json);
    }

    private static string GetProblemTypeUri(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        StatusCodes.Status401Unauthorized => "https://tools.ietf.org/html/rfc9110#section-15.5.2",
        StatusCodes.Status403Forbidden => "https://tools.ietf.org/html/rfc9110#section-15.5.4",
        StatusCodes.Status404NotFound => "https://tools.ietf.org/html/rfc9110#section-15.5.5",
        StatusCodes.Status409Conflict => "https://tools.ietf.org/html/rfc9110#section-15.5.10",
        StatusCodes.Status500InternalServerError => "https://tools.ietf.org/html/rfc9110#section-15.6.1",
        _ => "about:blank"
    };
}

// Custom exception types for explicit mapping

public class NotFoundException(string message) : Exception(message)
{
}

public class BadRequestException(string message) : Exception(message)
{
}

public class ConflictException(string message) : Exception(message)
{
}
