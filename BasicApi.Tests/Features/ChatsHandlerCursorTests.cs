using BasicApi.Features.Chats;
using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;
using BasicApi.Services;
using BasicApi.Storage.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BasicApi.Tests.Features;

public class ChatsHandlerCursorTests
{
    private readonly Mock<IChatService> _chatServiceMock;
    private readonly Mock<IChatRepository> _chatRepoMock;
    private readonly Mock<IMessageRepository> _msgRepoMock;
    private readonly ChatsHandler _handler;

    public ChatsHandlerCursorTests()
    {
        _chatServiceMock = new Mock<IChatService>();
        _chatRepoMock = new Mock<IChatRepository>();
        _msgRepoMock = new Mock<IMessageRepository>();
        _handler = new ChatsHandler(
            _chatServiceMock.Object,
            _chatRepoMock.Object,
            _msgRepoMock.Object);
    }

    [Fact]
    public async Task GetMessagesCursorAsync_WhenMember_ReturnsOkWithPaginatedResponse()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var messages = new List<MessageDto>
        {
            new() { Id = Guid.NewGuid(), SenderId = userId, Text = "Hello", CreatedAt = DateTime.UtcNow.AddMinutes(-5) },
            new() { Id = Guid.NewGuid(), SenderId = userId, Text = "World", CreatedAt = DateTime.UtcNow.AddMinutes(-2) },
        };

        var response = new CursorPaginatedResponse<MessageDto>
        {
            Items = messages,
            NextCursor = "some-cursor-value",
            HasMore = true
        };

        _chatServiceMock
            .Setup(s => s.GetChatMessagesCursorAsync(chatId, userId, It.IsAny<string?>(), 20))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.GetMessagesCursorAsync(chatId, userId, null, 20);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var paginated = Assert.IsType<CursorPaginatedResponse<MessageDto>>(okResult.Value);
        Assert.Equal(2, paginated.Items.Count);
        Assert.NotNull(paginated.NextCursor);
        Assert.True(paginated.HasMore);
    }

        [Fact]
    public async Task GetMessagesCursorAsync_WhenNotMember_ThrowsUnauthorizedAccess()
    {
        // Arrange
        _chatServiceMock
            .Setup(s => s.GetChatMessagesCursorAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<int>()))
                        .ThrowsAsync(new ForbiddenException("User is not a member of this chat"));

        // Act & Assert — the handler no longer catches this; it bubbles to middleware
        // which returns ProblemDetails with 403
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _handler.GetMessagesCursorAsync(Guid.NewGuid(), Guid.NewGuid(), null, 20));
        Assert.Contains("not a member", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ========== Search Messages ==========

    [Fact]
    public async Task SearchMessagesAsync_WhenSuccessful_ReturnsOkWithSearchResponse()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var query = "hello";
        var messages = new List<MessageDto>
        {
            new() { Id = Guid.NewGuid(), SenderId = userId, Text = "Hello world", CreatedAt = DateTime.UtcNow.AddMinutes(-5) },
            new() { Id = Guid.NewGuid(), SenderId = userId, Text = "Say hello", CreatedAt = DateTime.UtcNow.AddMinutes(-2) },
        };

        var response = new SearchMessagesResponseDto
        {
            Items = messages,
            NextCursor = "cursor-value",
            HasMore = false,
            Query = query,
            TotalCount = 2
        };

        _chatServiceMock
            .Setup(s => s.SearchChatMessagesCursorAsync(chatId, userId, query, null, 20))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.SearchMessagesAsync(chatId, userId, query, null, 20);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var searchResponse = Assert.IsType<SearchMessagesResponseDto>(okResult.Value);
        Assert.Equal(2, searchResponse.Items.Count);
        Assert.Equal(query, searchResponse.Query);
        Assert.Equal(2, searchResponse.TotalCount);
        Assert.False(searchResponse.HasMore);
    }

        [Fact]
    public async Task SearchMessagesAsync_WhenQueryTooShort_ThrowsBadRequest()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var query = "a";

        _chatServiceMock
            .Setup(s => s.SearchChatMessagesCursorAsync(chatId, userId, query, null, 20))
            .ThrowsAsync(new BadRequestException("Query must be at least 2 characters long"));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            _handler.SearchMessagesAsync(chatId, userId, query, null, 20));

        Assert.Contains("Query", ex.Message);
    }

    [Fact]
    public async Task SearchMessagesAsync_WhenNotMember_ThrowsForbiddenException()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        _chatServiceMock
            .Setup(s => s.SearchChatMessagesCursorAsync(chatId, userId, "hello", null, 20))
            .ThrowsAsync(new ForbiddenException("User is not a member of this chat"));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _handler.SearchMessagesAsync(chatId, userId, "hello", null, 20));

        Assert.Contains("not a member", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchMessagesAsync_WhenEmptyResult_ReturnsEmptyListWithNoCursor()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var query = "nonexistent";

        var response = new SearchMessagesResponseDto
        {
            Items = [],
            NextCursor = null,
            HasMore = false,
            Query = query,
            TotalCount = 0
        };

        _chatServiceMock
            .Setup(s => s.SearchChatMessagesCursorAsync(chatId, userId, query, null, 20))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.SearchMessagesAsync(chatId, userId, query, null, 20);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var searchResponse = Assert.IsType<SearchMessagesResponseDto>(okResult.Value);
        Assert.Empty(searchResponse.Items);
        Assert.Null(searchResponse.NextCursor);
        Assert.Equal(0, searchResponse.TotalCount);
    }
}
