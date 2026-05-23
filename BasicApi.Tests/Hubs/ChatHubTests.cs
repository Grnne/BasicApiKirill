using System.Security.Claims;
using BasicApi.Hubs;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;

namespace BasicApi.Tests.Hubs;

/// <summary>
/// Helper to capture SendAsync calls via the IClientProxy interface.
/// SignalR's SendAsync is an extension method (ClientProxyExtensions),
/// which Moq can't verify directly. Instead we use strict invocation tracking
/// via Verify on the underlying interface method with a custom matcher.
/// </summary>
public class ChatHubTests
{
        private readonly Mock<IChatRepository> _chatRepoMock;
        private readonly Mock<IMessageRepository> _messageRepoMock;
        private readonly Mock<ILogger<ChatHub>> _loggerMock;
        private readonly Mock<HubCallerContext> _contextMock;
        private readonly Mock<IHubCallerClients> _clientsMock;
        private readonly Mock<IGroupManager> _groupsMock;
        private readonly TestClientProxy _clientProxy;
        private readonly ChatHub _hub;
        private readonly Guid _userId;
        private readonly string _connectionId;

    private static int _connectionCounter;

        public ChatHubTests()
    {
        _connectionId = $"test-connection-id-{Interlocked.Increment(ref _connectionCounter)}";
        _userId = Guid.NewGuid();
        _chatRepoMock = new Mock<IChatRepository>();
        _messageRepoMock = new Mock<IMessageRepository>();
        _loggerMock = new Mock<ILogger<ChatHub>>();
        _contextMock = new Mock<HubCallerContext>();
        _clientsMock = new Mock<IHubCallerClients>();
        _groupsMock = new Mock<IGroupManager>();
        _clientProxy = new TestClientProxy();

        // Настраиваем контекст с authenticated user
        var claimsPrincipal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, _userId.ToString())
        ], "test"));

        _contextMock.Setup(c => c.User).Returns(claimsPrincipal);
        _contextMock.Setup(c => c.ConnectionId).Returns(_connectionId);
        // Подкладываем пустой FeatureCollection, чтобы GetHttpContext() не упал NRE
        _contextMock.Setup(c => c.Features).Returns(new FeatureCollection());

        // По умолчанию User() и Group() возвращают наш TestClientProxy
        _clientsMock
            .Setup(c => c.User(It.IsAny<string>()))
            .Returns(_clientProxy);

        _clientsMock
            .Setup(c => c.Group(It.IsAny<string>()))
            .Returns(_clientProxy);

        _hub = new ChatHub(_chatRepoMock.Object, _messageRepoMock.Object, _loggerMock.Object)
        {
            Context = _contextMock.Object,
            Clients = _clientsMock.Object,
            Groups = _groupsMock.Object
        };
    }

    #region OnConnectedAsync

    [Fact]
    public async Task OnConnectedAsync_WhenUserIdFound_SendsOnlineToAllChatMembers()
    {
        // Arrange
        var memberIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        _chatRepoMock
            .Setup(r => r.GetAllChatMembersAsync(_userId))
            .ReturnsAsync(memberIds);

        // Act
        await _hub.OnConnectedAsync();

        // Assert
        Assert.Equal(2, _clientProxy.Invocations.Count);
        Assert.All(_clientProxy.Invocations, inv =>
        {
            Assert.Equal("UserOnlineChanged", inv.Method);
            Assert.Equal(2, inv.Args.Length);
            Assert.Equal(_userId, inv.Args[0]);
            Assert.True((bool)inv.Args[1]!);
        });

        foreach (var memberId in memberIds)
        {
            _clientsMock.Verify(c => c.User(memberId.ToString()), Times.Once);
        }
    }

    [Fact]
    public async Task OnConnectedAsync_WhenNoChatMembers_SendsNothing()
    {
        // Arrange
        _chatRepoMock
            .Setup(r => r.GetAllChatMembersAsync(_userId))
            .ReturnsAsync(new List<Guid>());

        // Act
        await _hub.OnConnectedAsync();

        // Assert
        Assert.Empty(_clientProxy.Invocations);
    }

    [Fact]
        public async Task OnConnectedAsync_WhenUnauthenticated_DoesNotSendOnline()
    {
        // Arrange
        var unauthenticatedContext = new Mock<HubCallerContext>();
        unauthenticatedContext.Setup(c => c.User).Returns(new ClaimsPrincipal());
        unauthenticatedContext.Setup(c => c.Features).Returns(new FeatureCollection());

        var hub = new ChatHub(_chatRepoMock.Object, _messageRepoMock.Object, _loggerMock.Object)
        {
            Context = unauthenticatedContext.Object,
            Clients = _clientsMock.Object,
            Groups = _groupsMock.Object
        };

        // Act
        await hub.OnConnectedAsync();

        // Assert
        _chatRepoMock.Verify(r => r.GetAllChatMembersAsync(It.IsAny<Guid>()), Times.Never);
        Assert.Empty(_clientProxy.Invocations);
    }

    #endregion

    #region OnDisconnectedAsync

    [Fact]
    public async Task OnDisconnectedAsync_WhenNoOtherConnection_SendsOfflineToAllChatMembers()
    {
        // Arrange
        // Сначала "подключаемся", чтобы появилась запись в _onlineUsers
        _chatRepoMock
            .Setup(r => r.GetAllChatMembersAsync(_userId))
            .ReturnsAsync(new List<Guid>());
        await _hub.OnConnectedAsync();
        _clientProxy.Invocations.Clear();

        // Теперь настраиваем возврат участников для дисконнекта
        var memberIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        _chatRepoMock
            .Setup(r => r.GetAllChatMembersAsync(_userId))
            .ReturnsAsync(memberIds);

        // Act
        await _hub.OnDisconnectedAsync(null);

        // Assert
        Assert.Equal(2, _clientProxy.Invocations.Count);
        Assert.All(_clientProxy.Invocations, inv =>
        {
            Assert.Equal("UserOnlineChanged", inv.Method);
            Assert.Equal(_userId, inv.Args[0]);
            Assert.False((bool)inv.Args[1]!);
        });

        foreach (var memberId in memberIds)
        {
            _clientsMock.Verify(c => c.User(memberId.ToString()), Times.Once);
        }
    }

    [Fact]
        public async Task OnDisconnectedAsync_WhenUnauthenticated_DoesNotSendOffline()
    {
        // Arrange
        var unauthenticatedContext = new Mock<HubCallerContext>();
        unauthenticatedContext.Setup(c => c.User).Returns(new ClaimsPrincipal());
        unauthenticatedContext.Setup(c => c.Features).Returns(new FeatureCollection());

        var hub = new ChatHub(_chatRepoMock.Object, _messageRepoMock.Object, _loggerMock.Object)
        {
            Context = unauthenticatedContext.Object,
            Clients = _clientsMock.Object,
            Groups = _groupsMock.Object
        };

        // Act
        await hub.OnDisconnectedAsync(null);

        // Assert
        _chatRepoMock.Verify(r => r.GetAllChatMembersAsync(It.IsAny<Guid>()), Times.Never);
        Assert.Empty(_clientProxy.Invocations);
    }

    #endregion

    #region JoinChat

    [Fact]
    public async Task JoinChat_WhenMember_AddsToGroup()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        _chatRepoMock
            .Setup(r => r.IsMemberAsync(chatId, _userId))
            .ReturnsAsync(true);

        // Act
        await _hub.JoinChat(chatId);

        // Assert
        _groupsMock.Verify(
            g => g.AddToGroupAsync(_connectionId, chatId.ToString(), default),
            Times.Once);
    }

    [Fact]
    public async Task JoinChat_WhenNotMember_DoesNotAddToGroup()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        _chatRepoMock
            .Setup(r => r.IsMemberAsync(chatId, _userId))
            .ReturnsAsync(false);

        // Act
        await _hub.JoinChat(chatId);

        // Assert
        _groupsMock.Verify(
            g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), default),
            Times.Never);
    }

        private static Mock<HubCallerContext> CreateUnauthenticatedContext()
        {
            var ctx = new Mock<HubCallerContext>();
            ctx.Setup(c => c.User).Returns(new ClaimsPrincipal());
            ctx.Setup(c => c.Features).Returns(new FeatureCollection());
            return ctx;
        }

    [Fact]
    public async Task JoinChat_WhenUnauthenticated_DoesNotAddToGroup()
    {
        // Arrange
        var unauthenticatedContext = CreateUnauthenticatedContext();

        var hub = new ChatHub(_chatRepoMock.Object, _messageRepoMock.Object, _loggerMock.Object)
        {
            Context = unauthenticatedContext.Object,
            Clients = _clientsMock.Object,
            Groups = _groupsMock.Object
        };

        // Act
        await hub.JoinChat(Guid.NewGuid());

        // Assert
        _chatRepoMock.Verify(r => r.IsMemberAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
        _groupsMock.Verify(
            g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), default),
            Times.Never);
    }

    #endregion

    #region LeaveChat

    [Fact]
        public async Task LeaveChat_RemovesFromGroup()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        _chatRepoMock
            .Setup(r => r.IsMemberAsync(chatId, _userId))
            .ReturnsAsync(true);

        // Act
        await _hub.LeaveChat(chatId);

        // Assert
        _groupsMock.Verify(
            g => g.RemoveFromGroupAsync(_connectionId, chatId.ToString(), default),
            Times.Once);
    }

    #endregion

    #region SendMessage

    [Fact]
    public async Task SendMessage_WhenMember_CreatesAndSendsMessage()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        var text = "Hello!";
        var senderName = "Test User";
        var otherUserId = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.IsMemberAsync(chatId, _userId))
            .ReturnsAsync(true);

        _messageRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<Message>()))
            .ReturnsAsync(Guid.NewGuid());

        _chatRepoMock
            .Setup(r => r.GetUserNameAsync(_userId))
            .ReturnsAsync(senderName);

        _chatRepoMock
            .Setup(r => r.GetChatParticipantsAsync(chatId))
            .ReturnsAsync([
                new BasicApi.Storage.Dto.ChatParticipantDto(_userId, "Me", "me"),
                new BasicApi.Storage.Dto.ChatParticipantDto(otherUserId, "Other", "other")
            ]);

        // Act
        await _hub.SendMessage(chatId, text);

        // Assert
        _messageRepoMock.Verify(
            r => r.CreateAsync(It.Is<Message>(m =>
                m.ChatId == chatId &&
                m.SenderId == _userId &&
                m.Text == text &&
                !m.IsDeleted)),
            Times.Once);

                // Должно быть 3 ивента: MessageCreated в группу + ChatListUpdated себе + ChatListUpdated другому
                Assert.Equal(3, _clientProxy.Invocations.Count);

                // Первый — MessageCreated в группу
                Assert.Equal("MessageCreated", _clientProxy.Invocations[0].Method);
                var dto = Assert.IsType<BasicApi.Models.Dto.Message.MessageDto>(_clientProxy.Invocations[0].Args[0]);
                Assert.Equal(text, dto.Text);
                Assert.Equal(senderName, dto.SenderName);
                Assert.Equal(_userId, dto.SenderId);

                // Второй — ChatListUpdated себе
                Assert.Equal("ChatListUpdated", _clientProxy.Invocations[1].Method);
                Assert.Equal(chatId, _clientProxy.Invocations[1].Args[0]);
                var selfUpdate = Assert.IsType<BasicApi.Models.Dto.Message.MessageDto>(_clientProxy.Invocations[1].Args[1]);
                Assert.Equal(text, selfUpdate.Text);

                // Третий — ChatListUpdated другому участнику
                Assert.Equal("ChatListUpdated", _clientProxy.Invocations[2].Method);
                Assert.Equal(chatId, _clientProxy.Invocations[2].Args[0]);
                var otherUpdate = Assert.IsType<BasicApi.Models.Dto.Message.MessageDto>(_clientProxy.Invocations[2].Args[1]);
                Assert.Equal(text, otherUpdate.Text);
    }

    [Fact]
    public async Task SendMessage_WhenNotMember_DoesNotCreateOrSend()
    {
        // Arrange
        _chatRepoMock
            .Setup(r => r.IsMemberAsync(It.IsAny<Guid>(), _userId))
            .ReturnsAsync(false);

        // Act
        await _hub.SendMessage(Guid.NewGuid(), "test");

        // Assert
        _messageRepoMock.Verify(r => r.CreateAsync(It.IsAny<Message>()), Times.Never);
        Assert.Empty(_clientProxy.Invocations);
    }

    [Fact]
        public async Task SendMessage_WhenUnauthenticated_DoesNotCreateOrSend()
    {
        // Arrange
        var unauthenticatedContext = CreateUnauthenticatedContext();

        var hub = new ChatHub(_chatRepoMock.Object, _messageRepoMock.Object, _loggerMock.Object)
        {
            Context = unauthenticatedContext.Object,
            Clients = _clientsMock.Object,
            Groups = _groupsMock.Object
        };

        // Act
        await hub.SendMessage(Guid.NewGuid(), "test");

        // Assert
        _chatRepoMock.Verify(r => r.IsMemberAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
        _messageRepoMock.Verify(r => r.CreateAsync(It.IsAny<Message>()), Times.Never);
        Assert.Empty(_clientProxy.Invocations);
    }

    #endregion

    #region Typing

    [Fact]
        public async Task Typing_SendsTypingChangedToGroup()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        _chatRepoMock
            .Setup(r => r.GetChatParticipantsAsync(chatId))
            .ReturnsAsync([
                new BasicApi.Storage.Dto.ChatParticipantDto(_userId, "Me", "me"),
                new BasicApi.Storage.Dto.ChatParticipantDto(otherUserId, "Other", "other")
            ]);

        // Act
        await _hub.Typing(chatId, true);

        // Assert
        var inv = Assert.Single(_clientProxy.Invocations);
        Assert.Equal("TypingChanged", inv.Method);
        Assert.Equal(3, inv.Args.Length);
        Assert.Equal(chatId, inv.Args[0]);
        Assert.Equal(_userId, inv.Args[1]);
        Assert.True((bool)inv.Args[2]!);
    }

    [Fact]
        public async Task Typing_WhenUnauthenticated_DoesNotSend()
    {
        // Arrange
        var unauthenticatedContext = CreateUnauthenticatedContext();

        var hub = new ChatHub(_chatRepoMock.Object, _messageRepoMock.Object, _loggerMock.Object)
        {
            Context = unauthenticatedContext.Object,
            Clients = _clientsMock.Object,
            Groups = _groupsMock.Object
        };

        // Act
        await hub.Typing(Guid.NewGuid(), false);

        // Assert
        Assert.Empty(_clientProxy.Invocations);
    }

    #endregion

    #region NotifyChatCreatedAsync (static)

    [Fact]
    public async Task NotifyChatCreatedAsync_SendsChatCreatedToAllRecipients()
    {
        // Arrange
        var hubContextMock = new Mock<IHubContext<ChatHub>>();
        var hubClientsMock = new Mock<IHubClients>();
        var clientProxy = new TestClientProxy();

        hubClientsMock
            .Setup(c => c.User(It.IsAny<string>()))
            .Returns(clientProxy);

        hubContextMock
            .Setup(c => c.Clients)
            .Returns(hubClientsMock.Object);

        var chatId = Guid.NewGuid();
        var recipientIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var dto = new BasicApi.Models.Dto.Chat.ChatCreatedEventDto
        {
            Type = "private",
            Title = null,
            CompanionName = "Alice"
        };

        // Act
        await ChatHub.NotifyChatCreatedAsync(hubContextMock.Object, chatId, dto, recipientIds);

        // Assert
        Assert.Equal(2, clientProxy.Invocations.Count);
        Assert.All(clientProxy.Invocations, inv =>
        {
            Assert.Equal("ChatCreated", inv.Method);
            Assert.Equal(2, inv.Args.Length);
            Assert.Equal(chatId, inv.Args[0]);
            Assert.Same(dto, inv.Args[1]);
        });

        foreach (var id in recipientIds)
        {
            hubClientsMock.Verify(c => c.User(id.ToString()), Times.Once);
        }
    }

    [Fact]
    public async Task NotifyChatCreatedAsync_EmptyRecipients_SendsNothing()
    {
        // Arrange
        var hubContextMock = new Mock<IHubContext<ChatHub>>();
        var hubClientsMock = new Mock<IHubClients>();
        var clientProxy = new TestClientProxy();

        hubClientsMock
            .Setup(c => c.User(It.IsAny<string>()))
            .Returns(clientProxy);

        hubContextMock
            .Setup(c => c.Clients)
            .Returns(hubClientsMock.Object);

        var dto = new BasicApi.Models.Dto.Chat.ChatCreatedEventDto { Type = "private" };

        // Act
        await ChatHub.NotifyChatCreatedAsync(hubContextMock.Object, Guid.NewGuid(), dto, []);

        // Assert
        Assert.Empty(clientProxy.Invocations);
    }

    #endregion
}

/// <summary>
/// A test implementation of IClientProxy that records all SendAsync calls.
/// SignalR's SendCoreAsync on IClientProxy is a real interface method,
/// not an extension method, so we can implement the interface directly and track invocations.
/// </summary>
public class TestClientProxy : IClientProxy
{
    public List<(string Method, object?[] Args)> Invocations { get; } = new();

    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
        Invocations.Add((method, args));
        return Task.CompletedTask;
    }
}
