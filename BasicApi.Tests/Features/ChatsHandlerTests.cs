using BasicApi.Features.Chats;
using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;
using BasicApi.Services;
using BasicApi.Storage.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BasicApi.Tests.Features;

public class ChatsHandlerTests
{
    private readonly Mock<IChatService> _chatServiceMock;
    private readonly Mock<IChatRepository> _chatRepoMock;
    private readonly Mock<IMessageRepository> _msgRepoMock;
    private readonly ChatsHandler _handler;

    public ChatsHandlerTests()
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
    public async Task CreatePrivateChatAsync_ExistingChat_ReturnsOk()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var existingChatId = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.GetPrivateChatAsync(userId, otherUserId))
            .ReturnsAsync(new BasicApi.Storage.Entities.Chat { Id = existingChatId });

        // Act
        var result = await _handler.CreatePrivateChatAsync(userId, otherUserId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<CreateChatResponseDto>(okResult.Value);
        Assert.Equal(existingChatId, response.ChatId);

        _chatRepoMock.Verify(r => r.CreateAsync(It.IsAny<BasicApi.Storage.Entities.Chat>(), It.IsAny<Guid[]>()), Times.Never);
    }

    [Fact]
    public async Task CreatePrivateChatAsync_SameUser_ThrowsBadRequest()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            _handler.CreatePrivateChatAsync(userId, userId));

        Assert.Contains("yourself", ex.Message);
    }

    [Fact]
    public async Task GetChatAsync_Success_ReturnsOk()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var chatDetail = new ChatDetailDto { ChatId = chatId, Type = "private" };

        _chatServiceMock
            .Setup(s => s.GetChatDetailsAsync(chatId, userId))
            .ReturnsAsync(chatDetail);

        // Act
        var result = await _handler.GetChatAsync(chatId, userId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<ChatDetailDto>(okResult.Value);
        Assert.Equal(chatId, dto.ChatId);
    }

    [Fact]
    public async Task MarkReadAsync_Success_ReturnsOk()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var lastMessageId = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.IsMemberAsync(chatId, userId))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.MarkReadAsync(chatId, userId, lastMessageId);

        // Assert
        Assert.IsType<OkResult>(result);
        _msgRepoMock.Verify(r => r.UpdateLastReadAsync(chatId, userId, lastMessageId), Times.Once);
    }

    [Fact]
    public async Task MarkReadAsync_NotMember_ThrowsUnauthorizedAccess()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.IsMemberAsync(chatId, userId))
            .ReturnsAsync(false);

        // Act & Assert
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _handler.MarkReadAsync(chatId, userId, Guid.NewGuid()));
    }

    [Fact]
    public async Task GetMessagesAtAsync_WithExistingMessage_CreatesCursorAndReturnsOk()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var date = new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var pivotId = Guid.NewGuid();
        var pivotCreatedAt = new DateTime(2024, 6, 14, 0, 0, 0, DateTimeKind.Utc);

        _msgRepoMock
            .Setup(r => r.GetFirstMessageBeforeDateAsync(chatId, date))
            .ReturnsAsync(new BasicApi.Storage.Entities.Message { Id = pivotId, CreatedAt = pivotCreatedAt });

        var response = new CursorPaginatedResponse<MessageDto> { Items = [], NextCursor = null, HasMore = false };
        _chatServiceMock
            .Setup(s => s.GetChatMessagesCursorAsync(chatId, userId, It.IsAny<string?>(), 20))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.GetMessagesAtAsync(chatId, userId, date, 20);

        // Assert — cursor был сформирован из pivot-сообщения
        var okResult = Assert.IsType<OkObjectResult>(result);
        _chatServiceMock.Verify(
            s => s.GetChatMessagesCursorAsync(chatId, userId, It.Is<string?>(c => c != null), 20),
            Times.Once);
    }
}
