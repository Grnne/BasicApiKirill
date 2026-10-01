using BasicApi.Features.Chats;
using BasicApi.Models.Dto.Chat;
using BasicApi.Services;
using BasicApi.Services.Events;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Interfaces;
using BasicApi.Tests.TestDoubles;
using Moq;

namespace BasicApi.Tests.Features.Chats;

public class ChatServiceSavedChatTests
{
    private readonly Mock<IChatRepository> _chatRepoMock = new();
    private readonly Mock<IChatEventPublisher> _eventsMock = new();
    private readonly ChatService _service;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _chatId = Guid.NewGuid();

    public ChatServiceSavedChatTests()
    {
        _chatRepoMock
            .Setup(r => r.GetChatListItemAsync(_chatId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatListResult { ChatId = _chatId, Type = "saved", CreatedAt = DateTime.UtcNow });

        _service = new ChatService(new FakeDbSession(), _chatRepoMock.Object, Mock.Of<IUserRepository>(),
            new ChatPolicy(new MembershipService(_chatRepoMock.Object)), Mock.Of<IPresenceService>(), _eventsMock.Object);
    }

    [Fact]
    public async Task FirstOpen_CreatesTheChat_AndTellsOnlyTheUsersOtherDevicesThroughSync()
    {
        _chatRepoMock.Setup(r => r.GetOrCreateSavedChatAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync((_chatId, true));

        var result = await _service.GetOrCreateSavedChatAsync(_userId);

        Assert.True(result.Created);
        Assert.Equal("saved", result.Chat.Type);
        _eventsMock.Verify(e => e.ChatCreatedAsync(_userId, It.Is<ChatListItemDto>(c => c.ChatId == _chatId),
            false, It.IsAny<CancellationToken>()), Times.Once);
        _eventsMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LaterOpens_ReturnTheSameChat_WithoutEvents()
    {
        _chatRepoMock.Setup(r => r.GetOrCreateSavedChatAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync((_chatId, false));

        var result = await _service.GetOrCreateSavedChatAsync(_userId);

        Assert.False(result.Created);
        Assert.Equal(_chatId, result.Chat.ChatId);
        _eventsMock.VerifyNoOtherCalls();
    }
}
