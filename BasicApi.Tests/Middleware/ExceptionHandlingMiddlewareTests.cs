using System.Text.Json;
using BasicApi.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Moq;

namespace BasicApi.Tests.Middleware;

public class ExceptionHandlingMiddlewareTests
{
    private static ExceptionHandlingMiddleware CreateMiddleware(
        Exception exception,
        out MemoryStream bodyStream,
        out DefaultHttpContext context,
        bool isDevelopment = false)
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
                // Throw the specified exception when the next delegate is called
                throw exception;
            },
            envMock.Object);

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
    public async Task NotFoundException_Returns404()
    {
        // Arrange
        var middleware = CreateMiddleware(new NotFoundException("Chat not found"),
            out var bodyStream, out var context);

        // Act
        var doc = await InvokeAndParseAsync(middleware, context, bodyStream);

        // Assert
        Assert.Equal(404, context.Response.StatusCode);
        Assert.Equal("Not Found", doc.GetProperty("title").GetString());
        Assert.Equal("Chat not found", doc.GetProperty("detail").GetString());
        Assert.Equal("application/problem+json", context.Response.ContentType);
    }

    [Fact]
    public async Task UnauthorizedAccessException_Returns403()
    {
        // Arrange
        var middleware = CreateMiddleware(new UnauthorizedAccessException("Access denied"),
            out var bodyStream, out var context);

        // Act
        var doc = await InvokeAndParseAsync(middleware, context, bodyStream);

        // Assert
        Assert.Equal(403, context.Response.StatusCode);
        Assert.Equal("Forbidden", doc.GetProperty("title").GetString());
        Assert.Equal("Access denied", doc.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task BadRequestException_Returns400()
    {
        // Arrange
        var middleware = CreateMiddleware(new BadRequestException("Invalid input"),
            out var bodyStream, out var context);

        // Act
        var doc = await InvokeAndParseAsync(middleware, context, bodyStream);

        // Assert
        Assert.Equal(400, context.Response.StatusCode);
        Assert.Equal("Bad Request", doc.GetProperty("title").GetString());
        Assert.Equal("Invalid input", doc.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task ConflictException_Returns409()
    {
        // Arrange
        var middleware = CreateMiddleware(new ConflictException("Already exists"),
            out var bodyStream, out var context);

        // Act
        var doc = await InvokeAndParseAsync(middleware, context, bodyStream);

        // Assert
        Assert.Equal(409, context.Response.StatusCode);
        Assert.Equal("Conflict", doc.GetProperty("title").GetString());
        Assert.Equal("Already exists", doc.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task GenericException_Returns500()
    {
        // Arrange
        var middleware = CreateMiddleware(new InvalidOperationException("Unexpected error"),
            out var bodyStream, out var context);

        // Act
        var doc = await InvokeAndParseAsync(middleware, context, bodyStream);

        // Assert
        Assert.Equal(500, context.Response.StatusCode);
        Assert.Equal("Internal Server Error", doc.GetProperty("title").GetString());
        Assert.Equal("An unexpected error occurred.", doc.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task GenericException_Development_ShowsDetails()
    {
        // Arrange
        var middleware = CreateMiddleware(
            new InvalidOperationException("Unexpected error"),
            out var bodyStream, out var context,
            isDevelopment: true);

        // Act
        var doc = await InvokeAndParseAsync(middleware, context, bodyStream);

        // Assert
        Assert.Equal(500, context.Response.StatusCode);
        Assert.Equal("Unexpected error", doc.GetProperty("detail").GetString());
        Assert.True(doc.TryGetProperty("stackTrace", out _));
    }

    [Fact]
    public async Task GenericException_Production_HidesStackTrace()
    {
        // Arrange
        var middleware = CreateMiddleware(
            new InvalidOperationException("Unexpected error"),
            out var bodyStream, out var context,
            isDevelopment: false);

        // Act
        var doc = await InvokeAndParseAsync(middleware, context, bodyStream);

        // Assert
        Assert.Equal("An unexpected error occurred.", doc.GetProperty("detail").GetString());
        Assert.False(doc.TryGetProperty("stackTrace", out _));
    }

    [Fact]
    public async Task Middleware_IncludesTraceId()
    {
        // Arrange
        var middleware = CreateMiddleware(new NotFoundException("test"),
            out var bodyStream, out var context);

        // Act
        var doc = await InvokeAndParseAsync(middleware, context, bodyStream);

        // Assert
        Assert.Equal("test-trace-id", doc.GetProperty("traceId").GetString());
    }
}
