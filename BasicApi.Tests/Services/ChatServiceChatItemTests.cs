using BasicApi.Middleware.Exceptions;
using BasicApi.Services;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Moq;

namespace BasicApi.Tests.Services;

/// <summary>
/// Тесты на получение одного элемента списка чатов (GET /api/chats/{chatId}/item)
/// и на то, что маппинг companion-полей не теряется в поиске чатов.
/// </summary>
public class ChatServiceChatItemTests
{
    private readonly Mock<IChatRepository> _chatRepoMock;
    private readonly Mock<IMessageRepository> _msgRepoMock;
    private readonly ChatService _service;

    public ChatServiceChatItemTests()
    {
        _chatRepoMock = new Mock<IChatRepository>();
        _msgRepoMock = new Mock<IMessageRepository>();
        _service = new ChatService(_chatRepoMock.Object, _msgRepoMock.Object);
    }

    // ========== GetChatListItemAsync ==========

    [Fact]
    public async Task GetChatListItemAsync_WhenMember_ReturnsMappedItem()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var companionId = Guid.NewGuid();
        var msgId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;

        _chatRepoMock.Setup(r => r.GetByIdAsync(chatId))
            .ReturnsAsync(new Chat { Id = chatId, Type = "private" });
        _chatRepoMock.Setup(r => r.IsMemberAsync(chatId, userId)).ReturnsAsync(true);
        _chatRepoMock.Setup(r => r.GetChatListItemAsync(chatId, userId))
            .ReturnsAsync(new ChatListResult
            {
                ChatId = chatId,
                Type = "private",
                Title = null,
                CompanionId = companionId,
                CompanionName = "Alice",
                CompanionUsername = "alice",
                UnreadCount = 4,
                CreatedAt = createdAt.AddDays(-1),
                LastMessageId = msgId,
                LastMessageSenderId = companionId,
                LastMessageText = "hi",
                LastMessageCreatedAt = createdAt,
                LastMessageSenderName = "Alice"
            });

        // Act
        var item = await _service.GetChatListItemAsync(chatId, userId);

        // Assert
        Assert.Equal(chatId, item.ChatId);
        Assert.Equal("private", item.Type);
        Assert.Equal(companionId, item.CompanionId);
        Assert.Equal("Alice", item.CompanionName);
        Assert.Equal("alice", item.CompanionUsername);
        Assert.Equal(4, item.UnreadCount);
        Assert.Equal(createdAt, item.LastActivityAt);
        Assert.NotNull(item.LastMessage);
        Assert.Equal(msgId, item.LastMessage!.Id);
        Assert.Equal(chatId, item.LastMessage.ChatId);
        Assert.Equal("hi", item.LastMessage.Text);
    }

    [Fact]
    public async Task GetChatListItemAsync_WhenChatMissing_ThrowsNotFound()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        _chatRepoMock.Setup(r => r.GetByIdAsync(chatId)).ReturnsAsync((Chat?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetChatListItemAsync(chatId, Guid.NewGuid()));

        Assert.Equal("CHAT_NOT_FOUND", ex.ErrorCode);
    }

    [Fact]
    public async Task GetChatListItemAsync_WhenNotMember_ThrowsForbidden()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        _chatRepoMock.Setup(r => r.GetByIdAsync(chatId))
            .ReturnsAsync(new Chat { Id = chatId, Type = "private" });
        _chatRepoMock.Setup(r => r.IsMemberAsync(chatId, userId)).ReturnsAsync(false);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetChatListItemAsync(chatId, userId));

        Assert.Equal("NOT_A_MEMBER", ex.ErrorCode);
        _chatRepoMock.Verify(r => r.GetChatListItemAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
    }

    // ========== SearchChatsAsync: companion-поля не должны теряться ==========

    [Fact]
    public async Task SearchChatsAsync_PrivateChat_KeepsCompanionIdAndUsername()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var companionId = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.SearchChatsBatchedAsync(userId, "ali", "private", 20))
            .ReturnsAsync([
                new ChatListResult
                {
                    ChatId = chatId,
                    Type = "private",
                    CompanionId = companionId,
                    CompanionName = "Alice",
                    CompanionUsername = "alice",
                    CreatedAt = DateTime.UtcNow
                }
            ]);

        _chatRepoMock
            .Setup(r => r.CountChatsByQueryAsync(userId, "ali", "private"))
            .ReturnsAsync(1);

        // Act
        var result = await _service.SearchChatsAsync(userId, "ali", "private", 20);

        // Assert — регрессия: раньше маппер поиска терял CompanionId/CompanionUsername
        var item = Assert.Single(result.Items);
        Assert.Equal(companionId, item.CompanionId);
        Assert.Equal("alice", item.CompanionUsername);
        Assert.Equal("Alice", item.CompanionName);
    }

    [Fact]
    public async Task GetUserChatsAsync_PrivateChat_ExposesCompanionUsername()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var companionId = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.GetUserChatsBatchedAsync(userId))
            .ReturnsAsync([
                new ChatListResult
                {
                    ChatId = Guid.NewGuid(),
                    Type = "private",
                    CompanionId = companionId,
                    CompanionName = "Alice",
                    CompanionUsername = "alice",
                    CreatedAt = DateTime.UtcNow
                }
            ]);

        // Act
        var chats = await _service.GetUserChatsAsync(userId);

        // Assert
        var item = Assert.Single(chats);
        Assert.Equal(companionId, item.CompanionId);
        Assert.Equal("alice", item.CompanionUsername);
    }
}
