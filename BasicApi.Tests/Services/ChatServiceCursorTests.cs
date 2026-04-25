using BasicApi.Models.Dto.Message;
using BasicApi.Services;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Interfaces;
using Moq;

namespace BasicApi.Tests.Services;

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

    private static MessageWithSender ToMessageWithSender(Storage.Entities.Message msg, string senderName)
        => new()
        {
            Id = msg.Id,
            ChatId = msg.ChatId,
            SenderId = msg.SenderId,
            Text = msg.Text,
            CreatedAt = msg.CreatedAt,
            IsDeleted = msg.IsDeleted,
            SenderName = senderName
        };

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

        var messages = new List<MessageWithSender>
        {
            ToMessageWithSender(new() { Id = Guid.NewGuid(), ChatId = chatId, SenderId = senderId, Text = "Alpha", CreatedAt = now.AddMinutes(-10) }, "TestUser"),
            ToMessageWithSender(new() { Id = Guid.NewGuid(), ChatId = chatId, SenderId = senderId, Text = "Bravo", CreatedAt = now.AddMinutes(-5) }, "TestUser"),
        };

        _chatRepoMock
            .Setup(r => r.IsMemberAsync(chatId, userId))
            .ReturnsAsync(true);

        _msgRepoMock
            .Setup(r => r.GetMessagesWithSenderCursorAsync(chatId, null, 20))
            .ReturnsAsync(new CursorResult<MessageWithSender>
            {
                Items = messages,
                Extra = null
            });

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

        var messages = new List<MessageWithSender>
        {
            ToMessageWithSender(new() { Id = Guid.NewGuid(), ChatId = chatId, SenderId = senderId, Text = "Page 1 A", CreatedAt = now.AddMinutes(-10) }, "User"),
            ToMessageWithSender(new() { Id = Guid.NewGuid(), ChatId = chatId, SenderId = senderId, Text = "Page 1 B", CreatedAt = now.AddMinutes(-9) }, "User"),
        };

        var extra = ToMessageWithSender(
            new() { Id = Guid.NewGuid(), ChatId = chatId, SenderId = senderId, Text = "Page 2 A", CreatedAt = now.AddMinutes(-20) },
            "User");

        _chatRepoMock
            .Setup(r => r.IsMemberAsync(chatId, userId))
            .ReturnsAsync(true);

        _msgRepoMock
            .Setup(r => r.GetMessagesWithSenderCursorAsync(chatId, null, 2))
            .ReturnsAsync(new CursorResult<MessageWithSender>
            {
                Items = messages,
                Extra = extra
            });

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

        var messages = new List<MessageWithSender>
        {
            ToMessageWithSender(new() { Id = Guid.NewGuid(), ChatId = chatId, SenderId = senderId, Text = "Only message", CreatedAt = now.AddMinutes(-5) }, "User"),
        };

        _chatRepoMock
            .Setup(r => r.IsMemberAsync(chatId, userId))
            .ReturnsAsync(true);

        _msgRepoMock
            .Setup(r => r.GetMessagesWithSenderCursorAsync(chatId, null, 20))
            .ReturnsAsync(new CursorResult<MessageWithSender>
            {
                Items = messages,
                Extra = null
            });

        // Act
        var result = await _service.GetChatMessagesCursorAsync(chatId, userId, null, 20);

        // Assert
        Assert.False(result.HasMore);
        Assert.NotNull(result.NextCursor);
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

        var messages = new List<MessageWithSender>
        {
            ToMessageWithSender(new() { Id = Guid.NewGuid(), ChatId = chatId, SenderId = senderId, Text = "Older msg", CreatedAt = beforeDate.AddDays(-1) }, "User"),
        };

        _chatRepoMock
            .Setup(r => r.IsMemberAsync(chatId, userId))
            .ReturnsAsync(true);

        _msgRepoMock
            .Setup(r => r.GetMessagesWithSenderCursorAsync(chatId, cursor, 20))
            .ReturnsAsync(new CursorResult<MessageWithSender>
            {
                Items = messages,
                Extra = null
            });

        // Act
        var result = await _service.GetChatMessagesCursorAsync(chatId, userId, cursor, 20);

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("Older msg", result.Items[0].Text);

        _msgRepoMock.Verify(
            r => r.GetMessagesWithSenderCursorAsync(chatId, cursor, 20),
            Times.Once);
    }
}
