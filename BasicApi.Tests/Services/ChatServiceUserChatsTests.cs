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
        DateTime createdAt)
    {
        return new ChatListResult
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
    }

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

}
