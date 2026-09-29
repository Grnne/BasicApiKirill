using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;
using BasicApi.Services;
using BasicApi.Services.Events;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Interfaces;
using Moq;

namespace BasicApi.Tests.Services;

public class MessageServicePageTests
{
    private readonly Mock<IChatRepository> _chatRepoMock;
    private readonly Mock<IMessageRepository> _msgRepoMock;
    private readonly MessageService _service;

    public MessageServicePageTests()
    {
        _chatRepoMock = new Mock<IChatRepository>();
        _msgRepoMock = new Mock<IMessageRepository>();
        _service = new MessageService(_msgRepoMock.Object, new MembershipService(_chatRepoMock.Object),
            new ChatPolicy(new MembershipService(_chatRepoMock.Object)), Mock.Of<IChatEventPublisher>());
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
            Seq = msg.CreatedAt.Ticks, // порядок номеров совпадает со временем, как у настоящих сообщений
            SenderName = senderName
        };

    [Fact]
        public async Task GetPageAsync_WhenNotMember_ThrowsForbiddenAccess()
    {
        // Arrange
        _chatRepoMock
            .Setup(r => r.IsMemberAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetPageAsync(Guid.NewGuid(), Guid.NewGuid(), null, 20));

        Assert.Contains("not a member", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetPageAsync_WhenMember_ReturnsMappedMessages()
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
            .Setup(r => r.IsMemberAsync(chatId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _msgRepoMock
            .Setup(r => r.GetMessagesWithSenderCursorAsync(chatId, null, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CursorResult<MessageWithSender>
            {
                Items = messages,
                Extra = null
            });

        // Act
        var result = await _service.GetPageAsync(chatId, userId, null, 20);

        // Assert
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("Alpha", result.Items[0].Text);
        Assert.Equal("Bravo", result.Items[1].Text);
        Assert.Equal("TestUser", result.Items[0].SenderName);
    }

    [Fact]
    public async Task GetPageAsync_WhenHasMore_SetsHasMoreTrueAndGeneratesCursor()
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
            .Setup(r => r.IsMemberAsync(chatId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _msgRepoMock
            .Setup(r => r.GetMessagesWithSenderCursorAsync(chatId, null, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CursorResult<MessageWithSender>
            {
                Items = messages,
                Extra = extra
            });

        // Act
        var result = await _service.GetPageAsync(chatId, userId, null, 2);

        // Assert
        Assert.True(result.HasMore);
        Assert.NotNull(result.NextCursor);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task GetPageAsync_WhenNoMorePages_SetsHasMoreFalseAndNoCursor()
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
            .Setup(r => r.IsMemberAsync(chatId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _msgRepoMock
            .Setup(r => r.GetMessagesWithSenderCursorAsync(chatId, null, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CursorResult<MessageWithSender>
            {
                Items = messages,
                Extra = null
            });

        // Act
        var result = await _service.GetPageAsync(chatId, userId, null, 20);

        // Assert — следующей страницы нет, значит и курсора на неё нет
        Assert.False(result.HasMore);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task GetPageAsync_WithValidCursor_PassesCursorToRepository()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        var beforeDate = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var cursor = MessageCursor.BeforeSeqOf(42).Encode();

        var messages = new List<MessageWithSender>
        {
            ToMessageWithSender(new() { Id = Guid.NewGuid(), ChatId = chatId, SenderId = senderId, Text = "Older msg", CreatedAt = beforeDate.AddDays(-1) }, "User"),
        };

        _chatRepoMock
            .Setup(r => r.IsMemberAsync(chatId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _msgRepoMock
            .Setup(r => r.GetMessagesWithSenderCursorAsync(chatId, 42L, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CursorResult<MessageWithSender>
            {
                Items = messages,
                Extra = null
            });

        // Act
        var result = await _service.GetPageAsync(chatId, userId, cursor, 20);

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("Older msg", result.Items[0].Text);

                _msgRepoMock.Verify(
            r => r.GetMessagesWithSenderCursorAsync(chatId, 42L, 20, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetPageAsync_WithOldFormatCursor_ContinuesFromThatMessage()
    {
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        _chatRepoMock.Setup(r => r.IsMemberAsync(chatId, userId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _msgRepoMock.Setup(r => r.GetSeqAsync(chatId, messageId, It.IsAny<CancellationToken>())).ReturnsAsync(17);
        _msgRepoMock
            .Setup(r => r.GetMessagesWithSenderCursorAsync(chatId, 17L, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CursorResult<MessageWithSender>());

        await _service.GetPageAsync(chatId, userId, MessageCursor.EncodeLegacy(DateTime.UtcNow, messageId), 20);

        _msgRepoMock.Verify(r => r.GetMessagesWithSenderCursorAsync(chatId, 17L, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPageAsync_WithOldFormatCursorOfAnotherChat_IsInvalidCursor()
    {
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _chatRepoMock.Setup(r => r.IsMemberAsync(chatId, userId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            _service.GetPageAsync(chatId, userId, MessageCursor.EncodeLegacy(DateTime.UtcNow, Guid.NewGuid()), 20));

        Assert.Equal("INVALID_CURSOR", ex.ErrorCode);
    }

    // ========== Search Chat Messages ==========

    [Fact]
    public async Task SearchAsync_WhenNotMember_ThrowsForbiddenAccess()
    {
        // Arrange
        _chatRepoMock
            .Setup(r => r.IsMemberAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.SearchAsync(Guid.NewGuid(), Guid.NewGuid(), "hello", null, 20));

        Assert.Contains("not a member", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchAsync_WhenQueryTooShort_ThrowsBadRequest()
    {
        // Arrange
        _chatRepoMock
            .Setup(r => r.IsMemberAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            _service.SearchAsync(Guid.NewGuid(), Guid.NewGuid(), "a", null, 20));

        Assert.Contains("Query", ex.Message);
    }

    [Fact]
    public async Task SearchAsync_WhenMember_ReturnsMappedSearchResults()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var query = "hello";

        var messages = new List<MessageWithSender>
        {
            ToMessageWithSender(new() { Id = Guid.NewGuid(), ChatId = chatId, SenderId = senderId, Text = "Hello world", CreatedAt = now.AddMinutes(-10) }, "TestUser"),
            ToMessageWithSender(new() { Id = Guid.NewGuid(), ChatId = chatId, SenderId = senderId, Text = "Say hello", CreatedAt = now.AddMinutes(-5) }, "TestUser"),
        };

        _chatRepoMock
            .Setup(r => r.IsMemberAsync(chatId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

                _msgRepoMock
            .Setup(r => r.SearchMessagesCursorAsync(chatId, query, null, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new CursorResult<MessageWithSender>
            {
                Items = messages,
                Extra = null
            }, 2));

        // Act
        var result = await _service.SearchAsync(chatId, userId, query, null, 20);

        // Assert
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("Hello world", result.Items[0].Text);
        Assert.Equal("Say hello", result.Items[1].Text);
        Assert.Equal("TestUser", result.Items[0].SenderName);
        Assert.Equal(query, result.Query);
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_WhenNoResults_ReturnsEmptyList()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var query = "nonexistent";

        _chatRepoMock
            .Setup(r => r.IsMemberAsync(chatId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

                _msgRepoMock
            .Setup(r => r.SearchMessagesCursorAsync(chatId, query, null, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new CursorResult<MessageWithSender>
            {
                Items = [],
                Extra = null
            }, 0));

        // Act
        var result = await _service.SearchAsync(chatId, userId, query, null, 20);

        // Assert
        Assert.Empty(result.Items);
        Assert.Null(result.NextCursor);
        Assert.False(result.HasMore);
        Assert.Equal(query, result.Query);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_WithCursor_PassesCursorToRepository()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        var query = "hello";
        var cursor = MessageCursor.BeforeSeqOf(42).Encode();

        var messages = new List<MessageWithSender>
        {
            ToMessageWithSender(new() { Id = Guid.NewGuid(), ChatId = chatId, SenderId = senderId, Text = "Older hello", CreatedAt = new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc) }, "User"),
        };

        _chatRepoMock
            .Setup(r => r.IsMemberAsync(chatId, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _msgRepoMock
            .Setup(r => r.SearchMessagesCursorAsync(chatId, query, 42L, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new CursorResult<MessageWithSender>
            {
                Items = messages,
                Extra = null
            }, 1));

        // Act
        var result = await _service.SearchAsync(chatId, userId, query, cursor, 20);

        // Assert
        Assert.Single(result.Items);
        _msgRepoMock.Verify(
            r => r.SearchMessagesCursorAsync(chatId, query, 42L, 20, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
