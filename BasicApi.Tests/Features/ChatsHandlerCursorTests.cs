using BasicApi.Features.Chats;
using BasicApi.Middleware.Exceptions;
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

    
}
