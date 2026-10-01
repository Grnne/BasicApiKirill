using BasicApi.Features.Chats;
using BasicApi.Middleware.Exceptions;
using BasicApi.Services;
using BasicApi.Services.Events;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using BasicApi.Tests.TestDoubles;
using Moq;

namespace BasicApi.Tests.Features.Chats;

/// <summary>A single chat list item (GET /api/chats/{chatId}/item) and companion fields in chat search.</summary>
public class ChatServiceChatItemTests
{
    private readonly Mock<IChatRepository> _chatRepoMock;
    private readonly ChatService _service;

    public ChatServiceChatItemTests()
    {
        _chatRepoMock = new Mock<IChatRepository>();
        _service = new ChatService(new FakeDbSession(), _chatRepoMock.Object, Mock.Of<IUserRepository>(), new ChatPolicy(new MembershipService(_chatRepoMock.Object)),
            Mock.Of<IPresenceService>(), Mock.Of<IChatEventPublisher>());
    }

    [Fact]
    public async Task GetChatListItemAsync_WhenMember_ReturnsMappedItem()
    {
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var companionId = Guid.NewGuid();
        var msgId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;

        _chatRepoMock.Setup(r => r.GetByIdAsync(chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Chat { Id = chatId, Type = "private" });
        _chatRepoMock.Setup(r => r.IsMemberAsync(chatId, userId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _chatRepoMock.Setup(r => r.GetChatListItemAsync(chatId, userId, It.IsAny<CancellationToken>()))
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
                LastActivityAt = createdAt,
                LastMessageSenderName = "Alice"
            });

        var item = await _service.GetChatListItemAsync(chatId, userId);

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
        var chatId = Guid.NewGuid();
        _chatRepoMock.Setup(r => r.GetByIdAsync(chatId, It.IsAny<CancellationToken>())).ReturnsAsync((Chat?)null);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetChatListItemAsync(chatId, Guid.NewGuid(), It.IsAny<CancellationToken>()));

        Assert.Equal("CHAT_NOT_FOUND", ex.ErrorCode);
    }

    [Fact]
    public async Task GetChatListItemAsync_WhenNotMember_ThrowsForbidden()
    {
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        _chatRepoMock.Setup(r => r.GetByIdAsync(chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Chat { Id = chatId, Type = "private" });
        _chatRepoMock.Setup(r => r.IsMemberAsync(chatId, userId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetChatListItemAsync(chatId, userId, It.IsAny<CancellationToken>()));

        Assert.Equal("NOT_A_MEMBER", ex.ErrorCode);
        _chatRepoMock.Verify(r => r.GetChatListItemAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchChatsAsync_PrivateChat_KeepsCompanionIdAndUsername()
    {
        var userId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var companionId = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.SearchChatsBatchedAsync(userId, "ali", "private", 20, It.IsAny<CancellationToken>()))
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
            .Setup(r => r.CountChatsByQueryAsync(userId, "ali", "private", It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await _service.SearchChatsAsync(userId, "ali", "private", 20);

        // Regression: the search mapper used to lose CompanionId/CompanionUsername
        var item = Assert.Single(result.Items);
        Assert.Equal(companionId, item.CompanionId);
        Assert.Equal("alice", item.CompanionUsername);
        Assert.Equal("Alice", item.CompanionName);
    }

    [Fact]
    public async Task GetUserChatsAsync_PrivateChat_ExposesCompanionUsername()
    {
        var userId = Guid.NewGuid();
        var companionId = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.GetUserChatsBatchedAsync(userId, It.IsAny<CancellationToken>()))
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

        var chats = await _service.GetUserChatsAsync(userId);

        var item = Assert.Single(chats);
        Assert.Equal(companionId, item.CompanionId);
        Assert.Equal("alice", item.CompanionUsername);
    }
}
