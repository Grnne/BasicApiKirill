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
        catch (UnauthorizedException ex)
        {
            SetWwwAuthenticateHeader(context);
            await WriteProblemDetailsAsync(context, StatusCodes.Status401Unauthorized,
                "Unauthorized", ex.Message, errorCode: ex.ErrorCode);
        }
        catch (ForbiddenException ex)
        {
            await WriteProblemDetailsAsync(context, StatusCodes.Status403Forbidden,
                "Forbidden", ex.Message, errorCode: ex.ErrorCode);
        }
        catch (NotFoundException ex)
        {
            await WriteProblemDetailsAsync(context, StatusCodes.Status404NotFound,
                "Not Found", ex.Message, errorCode: ex.ErrorCode);
        }
        catch (BadRequestException ex)
        {
            await WriteProblemDetailsAsync(context, StatusCodes.Status400BadRequest,
                "Bad Request", ex.Message, errorCode: ex.ErrorCode);
        }
        catch (ConflictException ex)
        {
            await WriteProblemDetailsAsync(context, StatusCodes.Status409Conflict,
                "Conflict", ex.Message, errorCode: ex.ErrorCode);
        }
        catch (Exception ex)
        {
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

