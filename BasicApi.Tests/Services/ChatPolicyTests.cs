using BasicApi.Middleware.Exceptions;
using BasicApi.Services;
using BasicApi.Services.Events;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using BasicApi.Tests.TestDoubles;
using Moq;

namespace BasicApi.Tests.Services;

public class ChatPolicyTests
{
    private readonly Mock<IChatRepository> _chatRepoMock = new();
    private readonly ChatPolicy _policy;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _contact = Guid.NewGuid();
    private readonly Guid _stranger = Guid.NewGuid();
    private readonly Guid _chatId = Guid.NewGuid();

    public ChatPolicyTests()
    {
        _chatRepoMock.WithMembersFromIsMember();
        _chatRepoMock
            .Setup(r => r.IsMemberAsync(_chatId, _userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _chatRepoMock
            .Setup(r => r.GetAllChatMembersAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_contact]);
        _policy = new ChatPolicy(new MembershipService(_chatRepoMock.Object));
    }

    [Fact]
    public async Task Member_CanReadAndPost()
    {
        Assert.True((await _policy.CanReadAsync(_userId, _chatId)).Allowed);
        Assert.True((await _policy.CanPostAsync(_userId, _chatId)).Allowed);
    }

    [Fact]
    public async Task NonMember_IsDenied_WithNotAMemberCode()
    {
        var read = await _policy.CanReadAsync(_stranger, _chatId);
        var post = await _policy.CanPostAsync(_stranger, _chatId);

        Assert.Equal((false, "NOT_A_MEMBER"), (read.Allowed, read.Code));
        Assert.Equal((false, "NOT_A_MEMBER"), (post.Allowed, post.Code));
    }

    [Fact]
    public async Task Demand_ThrowsForbidden_WithTheDecisionCode()
    {
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => _policy.DemandReadAsync(_stranger, _chatId));

        Assert.Equal("NOT_A_MEMBER", ex.ErrorCode);
        Assert.Equal("User is not a member of this chat", ex.Message);
    }

    [Fact]
    public async Task Presence_IsVisibleForSelfAndContacts_NotForStrangers()
    {
        var visible = await _policy.FilterPresenceVisibleAsync(_userId, [_userId, _contact, _stranger]);

        Assert.Equal(new HashSet<Guid> { _userId, _contact }, visible);
    }

    [Fact]
    public async Task PresenceAudience_IsTheContacts()
    {
        Assert.Equal([_contact], await _policy.GetPresenceAudienceAsync(_userId));
    }
}

/// <summary>
/// Services do not decide on their own what is allowed: any policy denial, with its code,
/// reaches the client and nothing is executed. This way the new rules of plan 2 (blocks,
/// group permissions) will work without changes in the services.
/// </summary>
public class ChatPolicyEnforcementTests
{
    private const string Code = "BLOCKED_FOR_TEST";
    private readonly Mock<IChatPolicy> _policyMock = new();
    private readonly Mock<IChatRepository> _chatRepoMock = new();
    private readonly Mock<IMessageRepository> _msgRepoMock = new();
    private readonly Mock<IChatEventPublisher> _eventsMock = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _chatId = Guid.NewGuid();

    public ChatPolicyEnforcementTests()
    {
        var deny = PolicyDecision.Deny(Code, "Denied by policy");
        _policyMock.Setup(p => p.CanReadAsync(_userId, _chatId, It.IsAny<CancellationToken>())).ReturnsAsync(deny);
        _policyMock.Setup(p => p.CanPostAsync(_userId, _chatId, It.IsAny<CancellationToken>())).ReturnsAsync(deny);

        // Membership exists, so it is the policy that denies.
        _chatRepoMock.Setup(r => r.IsMemberAsync(_chatId, _userId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _chatRepoMock.Setup(r => r.GetMemberIdsAsync(_chatId, It.IsAny<CancellationToken>())).ReturnsAsync([_userId]);
        _chatRepoMock.Setup(r => r.GetByIdAsync(_chatId, It.IsAny<CancellationToken>())).ReturnsAsync(new Chat { Id = _chatId });
    }

    private MessageService Messages() =>
        new(new FakeDbSession(), _msgRepoMock.Object, new MembershipService(_chatRepoMock.Object), _policyMock.Object, _eventsMock.Object, Mock.Of<IDraftRepository>(), Mock.Of<IGroupRepository>());

    private static async Task AssertDenied(Func<Task> action) =>
        Assert.Equal(Code, (await Assert.ThrowsAsync<ForbiddenException>(action)).ErrorCode);

    [Fact]
    public async Task Messages_AllOperationsAskThePolicy()
    {
        var messages = Messages();

        await AssertDenied(() => messages.SendAsync(_chatId, _userId, "hello"));
        await AssertDenied(() => messages.GetPageAsync(_chatId, _userId, null, 20));
        await AssertDenied(() => messages.GetPageAtAsync(_chatId, _userId, DateTime.UtcNow, 20));
        await AssertDenied(() => messages.SearchAsync(_chatId, _userId, "hello", null, 20));

        _msgRepoMock.VerifyNoOtherCalls();
        _eventsMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReadState_AllOperationsAskThePolicy()
    {
        var readState = new ReadStateService(
            new FakeDbSession(), _msgRepoMock.Object, _chatRepoMock.Object, _policyMock.Object, _eventsMock.Object);

        await AssertDenied(() => readState.MarkReadAsync(_chatId, _userId, Guid.NewGuid()));
        await AssertDenied(() => readState.SetMarkedUnreadAsync(_chatId, _userId, true));

        _msgRepoMock.VerifyNoOtherCalls();
        _chatRepoMock.Verify(r => r.SetMarkedUnreadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _eventsMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Chats_DetailsAndItemAskThePolicy()
    {
        var chats = new ChatService(new FakeDbSession(), _chatRepoMock.Object, Mock.Of<IUserRepository>(), _policyMock.Object,
            Mock.Of<IPresenceService>(), _eventsMock.Object);

        await AssertDenied(() => chats.GetChatDetailsAsync(_chatId, _userId));
        await AssertDenied(() => chats.GetChatListItemAsync(_chatId, _userId));
    }

    [Fact]
    public async Task Typing_AsksThePolicy()
    {
        var presence = new PresenceService(new UserStatusService(), new MembershipService(_chatRepoMock.Object),
            _policyMock.Object, _eventsMock.Object, NullLogger<PresenceService>.Instance);

        await AssertDenied(() => presence.SetTypingAsync(_chatId, _userId, true));
        _eventsMock.VerifyNoOtherCalls();
    }
}
