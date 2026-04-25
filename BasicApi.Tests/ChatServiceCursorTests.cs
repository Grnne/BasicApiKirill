using BasicApi.Models.Dto.Message;
using BasicApi.Services;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Moq;

namespace BasicApi.Tests;

public class ChatServiceCursorTests
{
    private readonly Mock<IChatRepository> _chatRepoMock;
    private readonly Mock<IMessageRepository> _msgRepoMock;
    private readonly ChatService _service;

    public ChatServiceCursorTests()
    {
        _chatRepoMock = new Mock<IChatRepository>();
        _msgRepoMock = new Mock<IMessageRepository>();
        _service = new ChatService(_chatRepoMock.Object, _msgRepoMock.Object);
    }

    [Fact]
    public async Task GetChatMessagesCursorAsync_WhenNotMember_ThrowsUnauthorizedAccess()
    {
        // Arrange
        _chatRepoMock
            .Setup(r => r.IsMemberAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
            .ReturnsAsync(false);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.GetChatMessagesCursorAsync(Guid.NewGuid(), Guid.NewGuid(), null, 20));

        Assert.Contains("not a member", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetChatMessagesCursorAsync_WhenMember_ReturnsMappedMessages()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var messages = new List<Message>
        {
            new() { Id = Guid.NewGuid(), ChatId = chatId, SenderId = senderId, Text = "Alpha", CreatedAt = now.AddMinutes(-10) },
            new() { Id = Guid.NewGuid(), ChatId = chatId, SenderId = senderId, Text = "Bravo", CreatedAt = now.AddMinutes(-5) },
        };

        _chatRepoMock
            .Setup(r => r.IsMemberAsync(chatId, userId))
            .ReturnsAsync(true);

        _msgRepoMock
            .Setup(r => r.GetMessagesCursorAsync(chatId, null, 20))
            .ReturnsAsync(new CursorResult<Message>
            {
                Items = messages,
                Extra = null
            });

        _chatRepoMock
            .Setup(r => r.GetUserNameAsync(senderId))
            .ReturnsAsync("TestUser");

        // Act
        var result = await _service.GetChatMessagesCursorAsync(chatId, userId, null, 20);

        // Assert
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("Alpha", result.Items[0].Text);
        Assert.Equal("Bravo", result.Items[1].Text);
        Assert.Equal("TestUser", result.Items[0].SenderName);
    }

    [Fact]
    public async Task GetChatMessagesCursorAsync_WhenHasMore_SetsHasMoreTrueAndGeneratesCursor()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var messages = new List<Message>
        {
            new() { Id = Guid.NewGuid(), ChatId = chatId, SenderId = senderId, Text = "Page 1 A", CreatedAt = now.AddMinutes(-10) },
            new() { Id = Guid.NewGuid(), ChatId = chatId, SenderId = senderId, Text = "Page 1 B", CreatedAt = now.AddMinutes(-9) },
        };

        // Extra record signals more pages exist
        var extra = new Message
        {
            Id = Guid.NewGuid(), ChatId = chatId, SenderId = senderId,
            Text = "Page 2 A", CreatedAt = now.AddMinutes(-20)
        };

        _chatRepoMock
            .Setup(r => r.IsMemberAsync(chatId, userId))
            .ReturnsAsync(true);

        _msgRepoMock
            .Setup(r => r.GetMessagesCursorAsync(chatId, null, 2))
            .ReturnsAsync(new CursorResult<Message>
            {
                Items = messages,
                Extra = extra
            });

        _chatRepoMock
            .Setup(r => r.GetUserNameAsync(senderId))
            .ReturnsAsync("User");

        // Act
        var result = await _service.GetChatMessagesCursorAsync(chatId, userId, null, 2);

        // Assert
        Assert.True(result.HasMore);
        Assert.NotNull(result.NextCursor);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task GetChatMessagesCursorAsync_WhenNoMorePages_SetsHasMoreFalseWithCursor()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var messages = new List<Message>
        {
            new() { Id = Guid.NewGuid(), ChatId = chatId, SenderId = senderId, Text = "Only message", CreatedAt = now.AddMinutes(-5) },
        };

        _chatRepoMock
            .Setup(r => r.IsMemberAsync(chatId, userId))
            .ReturnsAsync(true);

        _msgRepoMock
            .Setup(r => r.GetMessagesCursorAsync(chatId, null, 20))
            .ReturnsAsync(new CursorResult<Message>
            {
                Items = messages,
                Extra = null
            });

        _chatRepoMock
            .Setup(r => r.GetUserNameAsync(senderId))
            .ReturnsAsync("User");

        // Act
        var result = await _service.GetChatMessagesCursorAsync(chatId, userId, null, 20);

        // Assert
        Assert.False(result.HasMore);
        // Cursor is present because it encodes the position of the last item.
        // The client uses NextCursor + HasMore: HasMore=false means "no need to fetch now",
        // but the cursor is given so if new messages arrive later, the client can still
        // use this cursor position.
        Assert.NotNull(result.NextCursor);
    }

    [Fact]
    public async Task GetChatMessagesCursorAsync_MessagesOrderedChronologically()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        // Storage returns in DESC order (newest first)
        var messages = new List<Message>
        {
            new() { Id = Guid.NewGuid(), ChatId = chatId, SenderId = senderId, Text = "Second", CreatedAt = now.AddMinutes(-5) },
            new() { Id = Guid.NewGuid(), ChatId = chatId, SenderId = senderId, Text = "First",  CreatedAt = now.AddMinutes(-10) },
        };

        _chatRepoMock
            .Setup(r => r.IsMemberAsync(chatId, userId))
            .ReturnsAsync(true);

        _msgRepoMock
            .Setup(r => r.GetMessagesCursorAsync(chatId, null, 20))
            .ReturnsAsync(new CursorResult<Message>
            {
                Items = messages,
                Extra = null
            });

        _chatRepoMock
            .Setup(r => r.GetUserNameAsync(senderId))
            .ReturnsAsync("User");

        // Act
        var result = await _service.GetChatMessagesCursorAsync(chatId, userId, null, 20);

        // Assert — service should reorder ASC (oldest first)
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("First", result.Items[0].Text);
        Assert.Equal("Second", result.Items[1].Text);
    }

    [Fact]
    public async Task GetChatMessagesCursorAsync_EmptyChat_ReturnsEmptyPage()
    {
        // Arrange
        _chatRepoMock
            .Setup(r => r.IsMemberAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
            .ReturnsAsync(true);

        _msgRepoMock
            .Setup(r => r.GetMessagesCursorAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<int>()))
            .ReturnsAsync(new CursorResult<Message>
            {
                Items = [],
                Extra = null
            });

        // Act
        var result = await _service.GetChatMessagesCursorAsync(Guid.NewGuid(), Guid.NewGuid(), null, 20);

        // Assert
        Assert.Empty(result.Items);
        Assert.False(result.HasMore);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task GetChatMessagesCursorAsync_WithValidCursor_PassesCursorToRepository()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        var beforeDate = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var beforeId = Guid.NewGuid();
        var cursor = new CursorDto(beforeDate, beforeId).Encode();

        var messages = new List<Message>
        {
            new() { Id = Guid.NewGuid(), ChatId = chatId, SenderId = senderId, Text = "Older msg", CreatedAt = beforeDate.AddDays(-1) },
        };

        _chatRepoMock
            .Setup(r => r.IsMemberAsync(chatId, userId))
            .ReturnsAsync(true);

        _msgRepoMock
            .Setup(r => r.GetMessagesCursorAsync(chatId, cursor, 20))
            .ReturnsAsync(new CursorResult<Message>
            {
                Items = messages,
                Extra = null
            });

        _chatRepoMock
            .Setup(r => r.GetUserNameAsync(senderId))
            .ReturnsAsync("User");

        // Act
        var result = await _service.GetChatMessagesCursorAsync(chatId, userId, cursor, 20);

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("Older msg", result.Items[0].Text);

        // Verify the cursor was passed through
        _msgRepoMock.Verify(
            r => r.GetMessagesCursorAsync(chatId, cursor, 20),
            Times.Once);
    }
}
