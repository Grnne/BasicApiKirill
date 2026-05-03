using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;
using BasicApi.Services;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Interfaces;
using Moq;

namespace BasicApi.Tests.Services;

public class ChatServiceUserChatsTests
{
    private readonly Mock<IChatRepository> _chatRepoMock;
    private readonly Mock<IMessageRepository> _msgRepoMock;
    private readonly ChatService _service;

    public ChatServiceUserChatsTests()
    {
        _chatRepoMock = new Mock<IChatRepository>();
        _msgRepoMock = new Mock<IMessageRepository>();
        _service = new ChatService(_chatRepoMock.Object, _msgRepoMock.Object);
    }

    private static ChatListResult MakeRow(Guid chatId, string type, string? title,
        string? companionName, int unreadCount,
        Guid? lastMsgId, Guid? lastMsgSenderId, string? lastMsgText,
        DateTime? lastMsgCreatedAt, string? lastMsgSenderName,
        DateTime createdAt) => new()
    {
        ChatId = chatId,
        Type = type,
        Title = title,
        CompanionName = companionName,
        UnreadCount = unreadCount,
        LastMessageId = lastMsgId,
        LastMessageSenderId = lastMsgSenderId,
        LastMessageText = lastMsgText,
        LastMessageCreatedAt = lastMsgCreatedAt,
        LastMessageSenderName = lastMsgSenderName,
        CreatedAt = createdAt
    };

    [Fact]
    public async Task GetUserChatsAsync_ReturnsMappedChats()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var chatId = Guid.NewGuid();
        var msgId = Guid.NewGuid();
        var senderId = Guid.NewGuid();

        var rows = new List<ChatListResult>
        {
            MakeRow(chatId, "private", null, "Alice", 2,
                msgId, senderId, "Hello", now.AddMinutes(-5), "Alice",
                now.AddDays(-1))
        };

        _chatRepoMock
            .Setup(r => r.GetUserChatsBatchedAsync(userId))
            .ReturnsAsync(rows);

        // Act
        var result = await _service.GetUserChatsAsync(userId);

        // Assert
        var dto = Assert.Single(result);
        Assert.Equal(chatId, dto.ChatId);
        Assert.Equal("private", dto.Type);
        Assert.Null(dto.Title);
        Assert.Equal("Alice", dto.CompanionName);
        Assert.Equal(2, dto.UnreadCount);
        Assert.Equal(now.AddMinutes(-5), dto.LastActivityAt);

        Assert.NotNull(dto.LastMessage);
        Assert.Equal(msgId, dto.LastMessage.Id);
        Assert.Equal(senderId, dto.LastMessage.SenderId);
        Assert.Equal("Alice", dto.LastMessage.SenderName);
        Assert.Equal("Hello", dto.LastMessage.Text);
        Assert.Equal(now.AddMinutes(-5), dto.LastMessage.CreatedAt);
        Assert.False(dto.LastMessage.IsRead);
    }

    [Fact]
    public async Task GetUserChatsAsync_WhenNoLastMessage_LastMessageIsNull()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var chatId = Guid.NewGuid();

        var rows = new List<ChatListResult>
        {
            MakeRow(chatId, "group", "General Chat", null, 0,
                null, null, null, null, null,
                DateTime.UtcNow.AddDays(-10))
        };

        _chatRepoMock
            .Setup(r => r.GetUserChatsBatchedAsync(userId))
            .ReturnsAsync(rows);

        // Act
        var result = await _service.GetUserChatsAsync(userId);

        // Assert
        var dto = Assert.Single(result);
        Assert.Equal("General Chat", dto.Title);
        Assert.Equal("group", dto.Type);
        Assert.Null(dto.LastMessage);
        Assert.Equal(0, dto.UnreadCount);
    }

    // ========== SearchChatsAsync Tests ==========

    [Theory]
    [InlineData("group", "Team Alpha", null, 5)]
    [InlineData("private", null, "Alice Johnson", 3)]
    public async Task SearchChatsAsync_ByType_ReturnsMappedResults(
        string typeFilter, string? expectedTitle, string? expectedCompanion, int totalCount)
    {
        // Arrange
        var userId = Guid.NewGuid();
        var query = expectedCompanion is not null ? "alice" : "team";
        var chatId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var rows = new List<ChatListResult>
        {
            MakeRow(chatId, typeFilter, expectedTitle, expectedCompanion, 3,
                null, null, null, null, null,
                now.AddDays(-1))
        };

        _chatRepoMock
            .Setup(r => r.SearchChatsBatchedAsync(userId, query, typeFilter, 20))
            .ReturnsAsync(rows);

        _chatRepoMock
            .Setup(r => r.CountChatsByQueryAsync(userId, query, typeFilter))
            .ReturnsAsync(totalCount);

        // Act
        var result = await _service.SearchChatsAsync(userId, query, typeFilter, 20);

        // Assert
        Assert.Single(result.Items);
        Assert.Equal(typeFilter, result.Items[0].Type);
        Assert.Equal(expectedTitle, result.Items[0].Title);
        Assert.Equal(expectedCompanion, result.Items[0].CompanionName);
        Assert.Equal(3, result.Items[0].UnreadCount);
        Assert.Equal(query, result.Query);
        Assert.Equal(totalCount, result.TotalCount);
    }

    [Fact]
    public async Task SearchChatsAsync_NoType_SearchesBothAndMergesResults()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var query = "test";
        var now = DateTime.UtcNow;

        var allRows = new List<ChatListResult>
        {
            MakeRow(Guid.NewGuid(), "private", null, "Test User", 0,
                null, null, null, null, null,
                now.AddDays(-1)),
            MakeRow(Guid.NewGuid(), "group", "Test Group", null, 0,
                null, null, null, null, null,
                now.AddDays(-2))
        };

        _chatRepoMock
            .Setup(r => r.SearchChatsBatchedAsync(userId, query, null, 20))
            .ReturnsAsync(allRows);

        _chatRepoMock
            .Setup(r => r.CountChatsByQueryAsync(userId, query, null))
            .ReturnsAsync(2);

        // Act
        var result = await _service.SearchChatsAsync(userId, query, null, 20);

        // Assert
        Assert.Equal(2, result.Items.Count);
        // Should be sorted: private (now-1d) first, then group (now-2d)
        Assert.Equal("private", result.Items[0].Type);
        Assert.Equal("group", result.Items[1].Type);
        Assert.Equal(query, result.Query);
        Assert.Equal(2, result.TotalCount);

        _chatRepoMock.Verify(r => r.SearchChatsBatchedAsync(userId, query, null, 20), Times.Once);
        _chatRepoMock.Verify(r => r.CountChatsByQueryAsync(userId, query, null), Times.Once);
    }

    [Fact]
    public async Task SearchChatsAsync_EmptyQuery_ThrowsBadRequest()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BasicApi.Middleware.Exceptions.BadRequestException>(() =>
            _service.SearchChatsAsync(userId, "", null, 20));

        Assert.Contains("empty", ex.Message);
    }

    [Fact]
    public async Task SearchChatsAsync_NoResults_ReturnsEmptyList()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var query = "nonexistent";

        _chatRepoMock
            .Setup(r => r.SearchChatsBatchedAsync(userId, query, null, 20))
            .ReturnsAsync([]);

        _chatRepoMock
            .Setup(r => r.CountChatsByQueryAsync(userId, query, null))
            .ReturnsAsync(0);

        // Act
        var result = await _service.SearchChatsAsync(userId, query, null, 20);

        // Assert
        Assert.Empty(result.Items);
        Assert.Equal(query, result.Query);
        Assert.Equal(0, result.TotalCount);
    }
}
