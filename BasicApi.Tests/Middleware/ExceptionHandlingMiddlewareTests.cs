using System.Text.Json;
using BasicApi.Middleware;
using BasicApi.Middleware.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Moq;

namespace BasicApi.Tests.Middleware;

public class ExceptionHandlingMiddlewareTests
{
    private static ExceptionHandlingMiddleware CreateMiddleware(
        Exception exception,
        out MemoryStream bodyStream,
        out DefaultHttpContext context,
        bool isDevelopment = false,
        FakeLogger<ExceptionHandlingMiddleware>? logger = null)
    {
        bodyStream = new MemoryStream();

        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns(isDevelopment ? "Development" : "Production");

        context = new DefaultHttpContext();
        context.Response.Body = bodyStream;
        context.TraceIdentifier = "test-trace-id";
        context.Request.Path = "/api/test";

        var middleware = new ExceptionHandlingMiddleware(
            innerContext =>
            {
                throw exception;
            },
            envMock.Object,
            logger ?? new FakeLogger<ExceptionHandlingMiddleware>());

        return middleware;
    }

    private static async Task<JsonElement> InvokeAndParseAsync(ExceptionHandlingMiddleware middleware,
        DefaultHttpContext context, MemoryStream bodyStream)
    {
        await middleware.InvokeAsync(context);
        bodyStream.Position = 0;
        using var reader = new StreamReader(bodyStream);
        var json = await reader.ReadToEndAsync();
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    [Fact]
    public async Task NotFoundException_Returns404_WithErrorCode()
    {
        var middleware = CreateMiddleware(new NotFoundException("Chat not found"),
            out var bodyStream, out var context);

        var doc = await InvokeAndParseAsync(middleware, context, bodyStream);

        Assert.Equal(404, context.Response.StatusCode);
        Assert.Equal("Not Found", doc.GetProperty("title").GetString());
        Assert.Equal("Chat not found", doc.GetProperty("detail").GetString());
        Assert.Equal("NOT_FOUND", doc.GetProperty("errorCode").GetString());
        Assert.Equal("application/problem+json", context.Response.ContentType);
    }

    [Fact]
    public async Task NotFoundException_SpecificErrorCode_IsReturned()
    {
        var middleware = CreateMiddleware(new NotFoundException("Chat not found", "CHAT_NOT_FOUND"),
            out var bodyStream, out var context);

        var doc = await InvokeAndParseAsync(middleware, context, bodyStream);

        Assert.Equal("CHAT_NOT_FOUND", doc.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task UnauthorizedException_Returns401_WithWwwAuthenticateHeader()
    {
        var middleware = CreateMiddleware(new UnauthorizedException("Authentication required"),
            out var bodyStream, out var context);

        var doc = await InvokeAndParseAsync(middleware, context, bodyStream);

        Assert.Equal(401, context.Response.StatusCode);
        Assert.Equal("Unauthorized", doc.GetProperty("title").GetString());
        Assert.Equal("Authentication required", doc.GetProperty("detail").GetString());
        Assert.Equal("UNAUTHORIZED", doc.GetProperty("errorCode").GetString());

        Assert.True(context.Response.Headers.ContainsKey("WWW-Authenticate"));
        Assert.Equal(
            "Bearer realm=\"basicapi\", error=\"invalid_token\", error_description=\"Authentication required\"",
            context.Response.Headers["WWW-Authenticate"]);
    }

    [Fact]
    public async Task UnauthorizedException_SpecificErrorCode_IsReturned()
    {
        var middleware = CreateMiddleware(new UnauthorizedException("Invalid credentials", "INVALID_CREDENTIALS"),
            out var bodyStream, out var context);

        var doc = await InvokeAndParseAsync(middleware, context, bodyStream);

        Assert.Equal("INVALID_CREDENTIALS", doc.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task ForbiddenException_Returns403_WithErrorCode()
    {
        var middleware = CreateMiddleware(new ForbiddenException("Access denied"),
            out var bodyStream, out var context);

        var doc = await InvokeAndParseAsync(middleware, context, bodyStream);

        Assert.Equal(403, context.Response.StatusCode);
        Assert.Equal("Forbidden", doc.GetProperty("title").GetString());
        Assert.Equal("Access denied", doc.GetProperty("detail").GetString());
        Assert.Equal("FORBIDDEN", doc.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task ForbiddenException_SpecificErrorCode_IsReturned()
    {
        var middleware = CreateMiddleware(new ForbiddenException("Not a member", "NOT_A_MEMBER"),
            out var bodyStream, out var context);

        var doc = await InvokeAndParseAsync(middleware, context, bodyStream);

        Assert.Equal("NOT_A_MEMBER", doc.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task BadRequestException_Returns400_WithErrorCode()
    {
        var middleware = CreateMiddleware(new BadRequestException("Invalid input"),
            out var bodyStream, out var context);

        var doc = await InvokeAndParseAsync(middleware, context, bodyStream);

        Assert.Equal(400, context.Response.StatusCode);
        Assert.Equal("Bad Request", doc.GetProperty("title").GetString());
        Assert.Equal("Invalid input", doc.GetProperty("detail").GetString());
        Assert.Equal("BAD_REQUEST", doc.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task BadRequestException_SpecificErrorCode_IsReturned()
    {
        var middleware = CreateMiddleware(new BadRequestException("Validation failed", "VALIDATION_ERROR"),
            out var bodyStream, out var context);

        var doc = await InvokeAndParseAsync(middleware, context, bodyStream);

        Assert.Equal("VALIDATION_ERROR", doc.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task ConflictException_Returns409_WithErrorCode()
    {
        var middleware = CreateMiddleware(new ConflictException("Already exists"),
            out var bodyStream, out var context);

        var doc = await InvokeAndParseAsync(middleware, context, bodyStream);

        Assert.Equal(409, context.Response.StatusCode);
        Assert.Equal("Conflict", doc.GetProperty("title").GetString());
        Assert.Equal("Already exists", doc.GetProperty("detail").GetString());
        Assert.Equal("CONFLICT", doc.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task ConflictException_SpecificErrorCode_IsReturned()
    {
        var middleware = CreateMiddleware(new ConflictException("Username taken", "USERNAME_TAKEN"),
            out var bodyStream, out var context);

        var doc = await InvokeAndParseAsync(middleware, context, bodyStream);

        Assert.Equal("USERNAME_TAKEN", doc.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task GenericException_Returns500_WithInternalErrorCode()
    {
        var middleware = CreateMiddleware(new InvalidOperationException("Unexpected error"),
            out var bodyStream, out var context);

        var doc = await InvokeAndParseAsync(middleware, context, bodyStream);

        Assert.Equal(500, context.Response.StatusCode);
        Assert.Equal("Internal Server Error", doc.GetProperty("title").GetString());
        Assert.Equal("An unexpected error occurred.", doc.GetProperty("detail").GetString());
        Assert.Equal("INTERNAL_ERROR", doc.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task GenericException_Development_ShowsDetails()
    {
        var middleware = CreateMiddleware(
            new InvalidOperationException("Unexpected error"),
            out var bodyStream, out var context,
            isDevelopment: true);

        var doc = await InvokeAndParseAsync(middleware, context, bodyStream);

        Assert.Equal(500, context.Response.StatusCode);
        Assert.Equal("Unexpected error", doc.GetProperty("detail").GetString());
        Assert.Equal("INTERNAL_ERROR", doc.GetProperty("errorCode").GetString());
        Assert.True(doc.TryGetProperty("stackTrace", out _));
    }

    [Fact]
    public async Task GenericException_Production_HidesStackTrace()
    {
        var middleware = CreateMiddleware(
            new InvalidOperationException("Unexpected error"),
            out var bodyStream, out var context,
            isDevelopment: false);

        var doc = await InvokeAndParseAsync(middleware, context, bodyStream);

        Assert.Equal("An unexpected error occurred.", doc.GetProperty("detail").GetString());
        Assert.Equal("INTERNAL_ERROR", doc.GetProperty("errorCode").GetString());
        Assert.False(doc.TryGetProperty("stackTrace", out _));
    }

    [Fact]
    public async Task Middleware_IncludesTraceId()
    {
        var middleware = CreateMiddleware(new NotFoundException("test"),
            out var bodyStream, out var context);

        var doc = await InvokeAndParseAsync(middleware, context, bodyStream);

        Assert.Equal("test-trace-id", doc.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task WwwAuthenticateHeader_NotSet_ForNon401Responses()
    {
        var middleware = CreateMiddleware(new ForbiddenException("Access denied"),
            out var bodyStream, out var context);

        await middleware.InvokeAsync(context);

        Assert.False(context.Response.Headers.ContainsKey("WWW-Authenticate"));
    }

    [Fact]
    public async Task WwwAuthenticateHeader_NotSet_ForGenericException()
    {
        var middleware = CreateMiddleware(new InvalidOperationException("error"),
            out var bodyStream, out var context);

        await middleware.InvokeAsync(context);

        Assert.False(context.Response.Headers.ContainsKey("WWW-Authenticate"));
    }

    [Fact]
    public async Task GenericException_IsLoggedAsError_WithExceptionAndTraceId()
    {
        var logger = new FakeLogger<ExceptionHandlingMiddleware>();
        var exception = new InvalidOperationException("db is down");
        var middleware = CreateMiddleware(exception, out _, out var context, logger: logger);

        await middleware.InvokeAsync(context);

        var record = Assert.Single(logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Error, record.Level);
        Assert.Same(exception, record.Exception);
        Assert.Contains("test-trace-id", record.Message);
        Assert.Contains("/api/test", record.Message);
    }

    [Fact]
    public async Task DomainException_IsLoggedAsInformation_WithoutStackTrace()
    {
        var logger = new FakeLogger<ExceptionHandlingMiddleware>();
        var middleware = CreateMiddleware(new NotFoundException("Chat not found", "CHAT_NOT_FOUND"),
            out _, out var context, logger: logger);

        await middleware.InvokeAsync(context);

        var record = Assert.Single(logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Null(record.Exception);
        Assert.Contains("CHAT_NOT_FOUND", record.Message);
        Assert.Contains("404", record.Message);
    }

    [Fact]
    public async Task TooLargeBody_Returns413_NotAnError()
    {
        // Before: a 500 that Kestrel could not even send — the proxy answered 502 instead.
        var logger = new FakeLogger<ExceptionHandlingMiddleware>();
        var middleware = CreateMiddleware(
            new BadHttpRequestException("Request body too large. The max request body size is 65536 bytes.", 413),
            out var bodyStream, out var context, logger: logger);

        var doc = await InvokeAndParseAsync(middleware, context, bodyStream);

        Assert.Equal(413, context.Response.StatusCode);
        Assert.Equal("REQUEST_TOO_LARGE", doc.GetProperty("errorCode").GetString());
        var record = Assert.Single(logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Information, record.Level);
    }

    [Fact]
    public async Task BrokenBody_Returns400_NotAnError()
    {
        // A client that went away mid-body is not a server failure.
        var logger = new FakeLogger<ExceptionHandlingMiddleware>();
        var middleware = CreateMiddleware(new BadHttpRequestException("Unexpected end of request content.", 400),
            out var bodyStream, out var context, logger: logger);

        var doc = await InvokeAndParseAsync(middleware, context, bodyStream);

        Assert.Equal(400, context.Response.StatusCode);
        Assert.Equal("BAD_REQUEST", doc.GetProperty("errorCode").GetString());
        Assert.Equal(LogLevel.Information, Assert.Single(logger.Collector.GetSnapshot()).Level);
    }

    [Fact]
    public async Task ResponseAlreadyStarted_LogsAndRethrows_WithoutWritingBody()
    {
        // The headers have already gone to the client — writing ProblemDetails on top is not possible,
        // otherwise the secondary exception would hide the original one.
        var logger = new FakeLogger<ExceptionHandlingMiddleware>();
        var exception = new InvalidOperationException("failed mid-stream");
        var middleware = CreateMiddleware(exception, out var bodyStream, out var context, logger: logger);
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        Assert.Same(exception, thrown);
        Assert.Equal(0, bodyStream.Length);
        var record = Assert.Single(logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Error, record.Level);
    }

    [Fact]
    public async Task ClientAbortedRequest_IsNotLoggedAsError()
    {
        // The client closed the tab mid-request — this is not a server error.
        var logger = new FakeLogger<ExceptionHandlingMiddleware>();
        var middleware = CreateMiddleware(new OperationCanceledException(),
            out var bodyStream, out var context, logger: logger);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        context.RequestAborted = cts.Token;

        await middleware.InvokeAsync(context);

        Assert.Equal(499, context.Response.StatusCode);
        Assert.Equal(0, bodyStream.Length);
        Assert.DoesNotContain(logger.Collector.GetSnapshot(), r => r.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task OperationCanceled_WithoutClientAbort_IsServerError()
    {
        // A timeout inside the server (e.g. a DB command) is a real error.
        var logger = new FakeLogger<ExceptionHandlingMiddleware>();
        var middleware = CreateMiddleware(new OperationCanceledException(),
            out _, out var context, logger: logger);

        await middleware.InvokeAsync(context);

        Assert.Equal(500, context.Response.StatusCode);
        Assert.Equal(LogLevel.Error, Assert.Single(logger.Collector.GetSnapshot()).Level);
    }

    private sealed class StartedResponseFeature : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }
}
