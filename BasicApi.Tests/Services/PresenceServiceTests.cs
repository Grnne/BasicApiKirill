using BasicApi.Middleware.Exceptions;
using BasicApi.Services;
using BasicApi.Services.Events;
using BasicApi.Storage.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using BasicApi.Tests.TestDoubles;
using Moq;

namespace BasicApi.Tests.Services;

/// <summary>Connect, disconnect, "typing" and getting acquainted in a new chat.</summary>
public class PresenceServiceTests
{
    private readonly UserStatusService _status = new();
    private readonly Mock<IChatRepository> _chatRepoMock = new();
    private readonly Mock<IChatEventPublisher> _eventsMock = new();
    private readonly PresenceService _service;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _contactA = Guid.NewGuid();
    private readonly Guid _contactB = Guid.NewGuid();
    private readonly Guid _chatId = Guid.NewGuid();

    public PresenceServiceTests()
    {
        _chatRepoMock.WithMembersFromIsMember();
        _chatRepoMock
            .Setup(r => r.GetAllChatMembersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _chatRepoMock
            .Setup(r => r.GetAllChatMembersAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_contactA, _contactB]);
        _chatRepoMock
            .Setup(r => r.GetMemberIdsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _chatRepoMock
            .Setup(r => r.GetMemberIdsAsync(_chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_userId, _contactA]);
        _chatRepoMock
            .Setup(r => r.IsMemberAsync(_chatId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var membership = new MembershipService(_chatRepoMock.Object);
        _service = new PresenceService(_status, membership, new ChatPolicy(membership), _eventsMock.Object,
            NullLogger<PresenceService>.Instance);
    }

    private void VerifyOnline(Guid userId, bool isOnline, Guid[] recipients, Times times) =>
        _eventsMock.Verify(e => e.UserOnlineChangedAsync(userId, isOnline,
            It.Is<IReadOnlyCollection<Guid>>(r => r.Count == recipients.Length && recipients.All(r.Contains)),
            It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task FirstConnection_TellsAllContacts_UserIsOnline()
    {
        await _service.ConnectedAsync(_userId, "c1");

        VerifyOnline(_userId, true, [_contactA, _contactB], Times.Once());
    }

    [Fact]
    public async Task SecondConnection_TellsNobody()
    {
        await _service.ConnectedAsync(_userId, "c1");
        await _service.ConnectedAsync(_userId, "c2");

        VerifyOnline(_userId, true, [_contactA, _contactB], Times.Once());
    }

    [Fact]
    public async Task LastDisconnection_TellsAllContacts_UserIsOffline()
    {
        await _service.ConnectedAsync(_userId, "c1");
        await _service.ConnectedAsync(_userId, "c2");

        await _service.DisconnectedAsync(_userId, "c1");
        VerifyOnline(_userId, false, [_contactA, _contactB], Times.Never());

        await _service.DisconnectedAsync(_userId, "c2");
        VerifyOnline(_userId, false, [_contactA, _contactB], Times.Once());
    }

    [Fact]
    public async Task Typing_ByMember_IsStored_AndSentToOtherMembersOnly()
    {
        await _service.SetTypingAsync(_chatId, _userId, true);

        var typing = await _status.GetTypingStatusAsync([_chatId]);
        Assert.Contains(_userId, typing[_chatId]);
        _eventsMock.Verify(e => e.TypingChangedAsync(_chatId, _userId, true,
            It.Is<IReadOnlyCollection<Guid>>(r => r.Count == 1 && r.Contains(_contactA)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Typing_ByNonMember_IsRejected_AndNotStoredOrSent()
    {
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.SetTypingAsync(Guid.NewGuid(), _userId, true));

        Assert.Equal("NOT_A_MEMBER", ex.ErrorCode);
        _eventsMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LastDisconnection_WhileTyping_ClearsTyping_AndTellsChatMembers()
    {
        await _service.ConnectedAsync(_userId, "c1");
        await _service.SetTypingAsync(_chatId, _userId, true);

        await _service.DisconnectedAsync(_userId, "c1");

        Assert.Empty(await _status.GetTypingStatusAsync([_chatId]));
        _eventsMock.Verify(e => e.TypingChangedAsync(_chatId, _userId, false,
            It.Is<IReadOnlyCollection<Guid>>(r => r.Count == 1 && r.Contains(_contactA)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Introduce_BothOnline_EachLearnsTheOther()
    {
        await _status.SetUserOnlineStatusAsync(_userId, "c1", true);
        await _status.SetUserOnlineStatusAsync(_contactA, "c2", true);

        await _service.IntroduceAsync(_userId, _contactA);

        VerifyOnline(_userId, true, [_contactA], Times.Once());
        VerifyOnline(_contactA, true, [_userId], Times.Once());
    }

    [Fact]
    public async Task Introduce_OfflineUser_IsNotAnnounced()
    {
        // The client already treats "offline" as the default.
        await _status.SetUserOnlineStatusAsync(_userId, "c1", true);

        await _service.IntroduceAsync(_userId, _contactA);

        VerifyOnline(_userId, true, [_contactA], Times.Once());
        _eventsMock.Verify(e => e.UserOnlineChangedAsync(_contactA, It.IsAny<bool>(),
            It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConnectionInfo_ReportsCountAndCurrentConnection()
    {
        await _service.ConnectedAsync(_userId, "c1");
        await _service.ConnectedAsync(_userId, "c2");

        var info = await _service.GetConnectionInfoAsync(_userId, "c2");

        Assert.Equal(new ConnectionInfo(2, true), info);
    }
}
