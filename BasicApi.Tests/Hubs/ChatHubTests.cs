using System.Security.Claims;
using BasicApi.Features.Auth;
using BasicApi.Features.Messages;
using BasicApi.Hubs;
using BasicApi.Middleware.Exceptions;
using BasicApi.Services;
using BasicApi.Tests.TestDoubles;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BasicApi.Tests.Hubs;

/// <summary>
/// The hub is an adapter: these tests check that calls reach the right service, plus what the hub
/// owns itself (the session check on connect, groups, the call limit). The rules are tested in services.
/// </summary>
public class ChatHubTests
{
    private readonly Mock<IMessageService> _messagesMock = new();
    private readonly Mock<IPresenceService> _presenceMock = new();
    private readonly Mock<IChatPolicy> _policyMock = new();
    private readonly Mock<ISessionService> _sessionsMock = new();
    private readonly Mock<IGroupManager> _groupsMock = new();
    private readonly RecordingHubClients _clients = new();
    private readonly Mock<IHubCallerClients> _callerClientsMock = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _sessionFamilyId = Guid.NewGuid();
    private readonly string _connectionId = $"conn-{Guid.NewGuid():N}";

    public ChatHubTests()
    {
        _callerClientsMock.Setup(c => c.Caller).Returns(() => _clients.Client(_connectionId));
        _sessionsMock
            .Setup(s => s.IsSessionFamilyLiveAsync(_sessionFamilyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _presenceMock
            .Setup(p => p.GetConnectionInfoAsync(It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(new ConnectionInfo(1, true));
        _policyMock
            .Setup(p => p.CanReadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolicyDecision.Allow);
    }

    private Mock<HubCallerContext> Context(bool authenticated = true)
    {
        var context = new Mock<HubCallerContext>();
        var identity = authenticated
            ? new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, _userId.ToString()),
                new Claim(ClaimTypes.Sid, _sessionFamilyId.ToString())
            ], "test")
            : new ClaimsIdentity();
        context.Setup(c => c.User).Returns(new ClaimsPrincipal(identity));
        context.Setup(c => c.ConnectionId).Returns(_connectionId);
        context.Setup(c => c.Features).Returns(new FeatureCollection());
        return context;
    }

    private ChatHub Hub(Mock<HubCallerContext>? context = null, HubConnectionRegistry? registry = null) =>
        new(_messagesMock.Object, _presenceMock.Object, _policyMock.Object, _sessionsMock.Object,
            registry ?? new HubConnectionRegistry(), NullLogger<ChatHub>.Instance)
        {
            Context = (context ?? Context()).Object,
            Clients = _callerClientsMock.Object,
            Groups = _groupsMock.Object
        };

    [Fact]
    public async Task OnConnected_LiveSession_ReportsConnectionToPresence()
    {
        await Hub().OnConnectedAsync();

        _presenceMock.Verify(p => p.ConnectedAsync(_userId, _connectionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnConnected_RevokedSession_AbortsConnection_WithoutGoingOnline()
    {
        // The access token is still alive, but the login is already closed by logout.
        _sessionsMock
            .Setup(s => s.IsSessionFamilyLiveAsync(_sessionFamilyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var context = Context();

        await Hub(context).OnConnectedAsync();

        context.Verify(c => c.Abort(), Times.Once);
        _presenceMock.Verify(p => p.ConnectedAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OnConnected_Unauthenticated_DoesNothing()
    {
        await Hub(Context(authenticated: false)).OnConnectedAsync();

        _presenceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OnDisconnected_ReportsDisconnectionToPresence()
    {
        await Hub().OnDisconnectedAsync(null);

        _presenceMock.Verify(p => p.DisconnectedAsync(_userId, _connectionId), Times.Once);
    }

    [Fact]
    public async Task JoinChat_Member_AddsConnectionToChatGroup()
    {
        var chatId = Guid.NewGuid();

        await Hub().JoinChat(chatId);

        _policyMock.Verify(p => p.CanReadAsync(_userId, chatId, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _groupsMock.Verify(g => g.AddToGroupAsync(_connectionId, chatId.ToString(), It.IsAny<CancellationToken>()),
            Times.Once);
        _groupsMock.Verify(g => g.RemoveFromGroupAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task JoinChat_RemovedWhileJoining_LeavesTheGroupAgain()
    {
        // Found by the review: a removal between the check and the join took the connection out of
        // a group it was not in yet — then it joined, and got the chat's messages on.
        var chatId = Guid.NewGuid();
        _policyMock
            .SetupSequence(p => p.CanReadAsync(_userId, chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolicyDecision.Allow)
            .ReturnsAsync(PolicyDecision.Deny("NOT_A_MEMBER", "User is not a member of this chat"));

        await Assert.ThrowsAsync<ForbiddenException>(() => Hub().JoinChat(chatId));

        _groupsMock.Verify(g => g.RemoveFromGroupAsync(_connectionId, chatId.ToString(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task JoinChat_OverCallLimit_IsRejected_BeforeTheDatabase()
    {
        // Each join is a database query: unlimited, a few sockets took the whole connection pool.
        var hub = Hub();

        var refused = 0;
        for (var i = 0; i < 25; i++)
        {
            try { await hub.JoinChat(Guid.NewGuid()); }
            catch (HubException ex) when (ex.Message.StartsWith("RATE_LIMITED:")) { refused++; }
        }

        Assert.Equal(5, refused);
        _groupsMock.Verify(g => g.AddToGroupAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(20));
    }

    [Fact]
    public async Task OnConnected_OverTheConnectionsOfOneUser_IsAborted()
    {
        // The call limit is per connection: without a cap on connections a user multiplied it.
        var registry = new HubConnectionRegistry();
        for (var i = 0; i < ChatHub.MaxConnectionsPerUser; i++)
        {
            var other = Context();
            other.Setup(c => c.ConnectionId).Returns($"other-{i}");
            registry.Add(other.Object, _userId, _sessionFamilyId);
        }
        var context = Context();

        await Hub(context, registry).OnConnectedAsync();

        context.Verify(c => c.Abort(), Times.Once);
        _presenceMock.Verify(p => p.ConnectedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task JoinChat_NonMember_IsRejected_AndNotAddedToGroup()
    {
        var chatId = Guid.NewGuid();
        _policyMock
            .Setup(p => p.CanReadAsync(_userId, chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PolicyDecision.Deny("NOT_A_MEMBER", "User is not a member of this chat"));

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => Hub().JoinChat(chatId));

        Assert.Equal("NOT_A_MEMBER", ex.ErrorCode);

        _groupsMock.Verify(g => g.AddToGroupAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LeaveChat_RemovesConnectionFromChatGroup()
    {
        var chatId = Guid.NewGuid();

        await Hub().LeaveChat(chatId);

        _groupsMock.Verify(g => g.RemoveFromGroupAsync(_connectionId, chatId.ToString(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendMessage_GoesToMessageService()
    {
        var chatId = Guid.NewGuid();

        await Hub().SendMessage(chatId, "hello");

        _messagesMock.Verify(m => m.SendAsync(chatId, _userId, "hello", null, null, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendMessage_OverCallLimit_IsRejectedWithCode()
    {
        var hub = Hub();

        HubException? last = null;
        for (var i = 0; i < 25; i++)
        {
            try { await hub.SendMessage(Guid.NewGuid(), "x"); }
            catch (HubException ex) { last = ex; }
        }

        Assert.NotNull(last);
        Assert.StartsWith("RATE_LIMITED:", last.Message);
        _messagesMock.Verify(m => m.SendAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
            It.IsAny<IReadOnlyList<Models.Dto.Message.MessageEntityDto>?>(), It.IsAny<IReadOnlyList<Guid>?>(),
            It.IsAny<CancellationToken>()),
            Times.Exactly(20));
    }

    [Fact]
    public async Task Typing_GoesToPresenceService()
    {
        var chatId = Guid.NewGuid();

        await Hub().Typing(chatId, true);

        _presenceMock.Verify(p => p.SetTypingAsync(chatId, _userId, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Typing_OverCallLimit_IsIgnoredSilently()
    {
        var hub = Hub();

        for (var i = 0; i < 25; i++)
            await hub.Typing(Guid.NewGuid(), true);

        _presenceMock.Verify(p => p.SetTypingAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Exactly(20));
    }

    [Fact]
    public async Task Unauthenticated_CommandsDoNothing()
    {
        var hub = Hub(Context(authenticated: false));

        await hub.SendMessage(Guid.NewGuid(), "hello");
        await hub.Typing(Guid.NewGuid(), true);
        await hub.JoinChat(Guid.NewGuid());
        await hub.Ping();

        _messagesMock.VerifyNoOtherCalls();
        _presenceMock.VerifyNoOtherCalls();
        _policyMock.VerifyNoOtherCalls();
        Assert.Empty(_clients.Sent);
    }

    [Fact]
    public async Task Ping_AnswersCallerWithConnectionInfo()
    {
        _presenceMock
            .Setup(p => p.GetConnectionInfoAsync(_userId, _connectionId))
            .ReturnsAsync(new ConnectionInfo(2, true));

        await Hub().Ping();

        var pong = Assert.Single(_clients.Sent);
        Assert.Equal(("client", "Pong"), (pong.Target, pong.Method));
        Assert.Equal([_connectionId], pong.Ids);
        var payload = Assert.Single(pong.Args)!;
        Assert.Equal(2, payload.GetType().GetProperty("ConnectionCount")!.GetValue(payload));
        Assert.Equal(_userId, payload.GetType().GetProperty("UserId")!.GetValue(payload));
    }
}
