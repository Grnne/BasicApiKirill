using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Chat;
using BasicApi.Services;
using BasicApi.Services.Events;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using BasicApi.Tests.TestDoubles;
using Moq;

namespace BasicApi.Tests.Services;

public class ChatServicePrivateChatTests
{
    private readonly Mock<IChatRepository> _chatRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IPresenceService> _presenceMock = new();
    private readonly Mock<IChatEventPublisher> _eventsMock = new();
    private readonly ChatService _service;

    public ChatServicePrivateChatTests()
    {
        // By default any companion exists and is active
        _userRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new User { Id = id, IsActive = true });

        _service = new ChatService(new FakeDbSession(), _chatRepoMock.Object, _userRepoMock.Object,
            new ChatPolicy(new MembershipService(_chatRepoMock.Object)), _presenceMock.Object, _eventsMock.Object);
    }

    /// <summary>
    /// A private chat list-item row as the repository would return it
    /// for a specific viewer (companion is always "the other" participant).
    /// </summary>
    private static ChatListResult PrivateRow(Guid chatId, Guid companionId, string companionName, string companionUsername) => new()
    {
        ChatId = chatId,
        Type = "private",
        CompanionId = companionId,
        CompanionName = companionName,
        CompanionUsername = companionUsername,
        CreatedAt = DateTime.UtcNow
    };

    /// <summary>The repository returns different cards to the creator and the recipient (each has their own companion).</summary>
    private Guid ArrangeChat(Guid creatorId, Guid recipientId, bool created)
    {
        var chatId = Guid.NewGuid();
        _chatRepoMock
            .Setup(r => r.GetOrCreatePrivateChatAsync(creatorId, recipientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((chatId, created));
        _chatRepoMock
            .Setup(r => r.GetChatListItemAsync(chatId, creatorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PrivateRow(chatId, recipientId, "Alice", "alice"));
        _chatRepoMock
            .Setup(r => r.GetChatListItemAsync(chatId, recipientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PrivateRow(chatId, creatorId, "Bob", "bob"));
        return chatId;
    }

    [Fact]
    public async Task ExistingChat_ReturnsItsChatListItem_NotCreated_AndAnnouncesNothing()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var chatId = ArrangeChat(userId, otherUserId, created: false);

        var result = await _service.GetOrCreatePrivateChatAsync(userId, otherUserId);

        // A full list item, not a bare chatId
        Assert.False(result.Created);
        Assert.Equal(chatId, result.Chat.ChatId);
        Assert.Equal(otherUserId, result.Chat.CompanionId);
        Assert.Equal("Alice", result.Chat.CompanionName);
        Assert.Equal("alice", result.Chat.CompanionUsername);
        _eventsMock.VerifyNoOtherCalls();
        _presenceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NewChat_ReturnsCreatedItemForCreator()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var chatId = ArrangeChat(userId, otherUserId, created: true);

        var result = await _service.GetOrCreatePrivateChatAsync(userId, otherUserId);

        Assert.True(result.Created);
        Assert.Equal(chatId, result.Chat.ChatId);
        Assert.Equal("private", result.Chat.Type);
        Assert.Equal(otherUserId, result.Chat.CompanionId);
        Assert.Null(result.Chat.LastMessage);
        Assert.Equal(0, result.Chat.UnreadCount);
    }

    [Fact]
    public async Task NewChat_IsAnnouncedToTheOtherUserOnly_FromTheirPointOfView()
    {
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var chatId = ArrangeChat(userId, otherUserId, created: true);

        await _service.GetOrCreatePrivateChatAsync(userId, otherUserId);

        // Regression: the recipient must not be sent themselves as the companion
        _eventsMock.Verify(e => e.ChatCreatedAsync(otherUserId,
            It.Is<ChatListItemDto>(i => i.ChatId == chatId && i.CompanionId == userId && i.CompanionName == "Bob"),
            true, It.IsAny<CancellationToken>()), Times.Once);

        // For the creator - only a journal entry (for their other devices), no live event:
        // they got the card in the response.
        _eventsMock.Verify(e => e.ChatCreatedAsync(userId,
            It.Is<ChatListItemDto>(i => i.ChatId == chatId && i.CompanionId == otherUserId),
            false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NewChat_BothLearnEachOthersPresence()
    {
        // UserOnlineChanged used to be broadcast only on connect and only to those
        // who already shared a chat: in a new chat both saw each other as "offline".
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        ArrangeChat(userId, otherUserId, created: true);

        await _service.GetOrCreatePrivateChatAsync(userId, otherUserId);

        _presenceMock.Verify(p => p.IntroduceAsync(userId, otherUserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnknownCompanion_Is404_InsteadOf500()
    {
        // The insert used to fail on a foreign key and the client got 500.
        var otherUserId = Guid.NewGuid();
        _userRepoMock
            .Setup(r => r.GetByIdAsync(otherUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetOrCreatePrivateChatAsync(Guid.NewGuid(), otherUserId));

        Assert.Equal("USER_NOT_FOUND", ex.ErrorCode);
        _chatRepoMock.Verify(r => r.GetOrCreatePrivateChatAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeactivatedCompanion_Is404()
    {
        var otherUserId = Guid.NewGuid();
        _userRepoMock
            .Setup(r => r.GetByIdAsync(otherUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Id = otherUserId, IsActive = false });

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetOrCreatePrivateChatAsync(Guid.NewGuid(), otherUserId));

        Assert.Equal("USER_NOT_FOUND", ex.ErrorCode);
    }

    [Fact]
    public async Task WithYourself_IsBadRequest()
    {
        var userId = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            _service.GetOrCreatePrivateChatAsync(userId, userId));

        Assert.Equal("SELF_CHAT", ex.ErrorCode);
    }
}
