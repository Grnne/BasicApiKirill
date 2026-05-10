using BasicApi.Features.Chats;
using BasicApi.Hubs;
using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;
using BasicApi.Services;
using BasicApi.Storage.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace BasicApi.Tests.Features;

public class ChatsHandlerTests
{
    private readonly Mock<IChatService> _chatServiceMock;
    private readonly Mock<IChatRepository> _chatRepoMock;
    private readonly Mock<IMessageRepository> _msgRepoMock;
    private readonly Mock<IHubContext<ChatHub>> _hubContextMock;
    private readonly Mock<IHubClients> _hubClientsMock;
    private readonly TestClientProxy _clientProxy;
    private readonly ChatsHandler _handler;

    public ChatsHandlerTests()
    {
        _chatServiceMock = new Mock<IChatService>();
        _chatRepoMock = new Mock<IChatRepository>();
        _msgRepoMock = new Mock<IMessageRepository>();
        _hubContextMock = new Mock<IHubContext<ChatHub>>();
        _hubClientsMock = new Mock<IHubClients>();
        _clientProxy = new TestClientProxy();

        _hubClientsMock
            .Setup(c => c.User(It.IsAny<string>()))
            .Returns(_clientProxy);

        _hubContextMock
            .Setup(c => c.Clients)
            .Returns(_hubClientsMock.Object);

        _handler = new ChatsHandler(
            _chatServiceMock.Object,
            _chatRepoMock.Object,
            _msgRepoMock.Object,
            _hubContextMock.Object);
    }

    [Fact]
    public async Task CreatePrivateChatAsync_ExistingChat_ReturnsOk()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var existingChatId = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.GetPrivateChatAsync(userId, otherUserId))
            .ReturnsAsync(new BasicApi.Storage.Entities.Chat { Id = existingChatId });

        // Act
        var result = await _handler.CreatePrivateChatAsync(userId, otherUserId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<CreateChatResponseDto>(okResult.Value);
        Assert.Equal(existingChatId, response.ChatId);

        _chatRepoMock.Verify(r => r.CreateAsync(It.IsAny<BasicApi.Storage.Entities.Chat>(), It.IsAny<Guid[]>()), Times.Never);
    }

    [Fact]
    public async Task CreatePrivateChatAsync_SameUser_ThrowsBadRequest()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            _handler.CreatePrivateChatAsync(userId, userId));

        Assert.Contains("yourself", ex.Message);
    }

    [Fact]
    public async Task GetChatAsync_Success_ReturnsOk()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var chatDetail = new ChatDetailDto { ChatId = chatId, Type = "private" };

        _chatServiceMock
            .Setup(s => s.GetChatDetailsAsync(chatId, userId))
            .ReturnsAsync(chatDetail);

        // Act
        var result = await _handler.GetChatAsync(chatId, userId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<ChatDetailDto>(okResult.Value);
        Assert.Equal(chatId, dto.ChatId);
    }

    [Fact]
    public async Task MarkReadAsync_Success_ReturnsOk()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var lastMessageId = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.IsMemberAsync(chatId, userId))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.MarkReadAsync(chatId, userId, lastMessageId);

        // Assert
        Assert.IsType<OkResult>(result);
        _msgRepoMock.Verify(r => r.UpdateLastReadAsync(chatId, userId, lastMessageId), Times.Once);
    }

    [Fact]
    public async Task MarkReadAsync_NotMember_ThrowsUnauthorizedAccess()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.IsMemberAsync(chatId, userId))
            .ReturnsAsync(false);

        // Act & Assert
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _handler.MarkReadAsync(chatId, userId, Guid.NewGuid()));
    }

    [Fact]
    public async Task GetMessagesAtAsync_WithExistingMessage_CreatesCursorAndReturnsOk()
    {
        // Arrange
        var chatId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var date = new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var pivotId = Guid.NewGuid();
        var pivotCreatedAt = new DateTime(2024, 6, 14, 0, 0, 0, DateTimeKind.Utc);

        _msgRepoMock
            .Setup(r => r.GetFirstMessageBeforeDateAsync(chatId, date))
            .ReturnsAsync(new BasicApi.Storage.Entities.Message { Id = pivotId, CreatedAt = pivotCreatedAt });

        var response = new CursorPaginatedResponse<MessageDto> { Items = [], NextCursor = null, HasMore = false };
        _chatServiceMock
            .Setup(s => s.GetChatMessagesCursorAsync(chatId, userId, It.IsAny<string?>(), 20))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.GetMessagesAtAsync(chatId, userId, date, 20);

        // Assert — cursor был сформирован из pivot-сообщения
        var okResult = Assert.IsType<OkObjectResult>(result);
        _chatServiceMock.Verify(
            s => s.GetChatMessagesCursorAsync(chatId, userId, It.Is<string?>(c => c != null), 20),
            Times.Once);
    }

    // ========== Chat Created Event Tests ==========

    [Fact]
    public async Task CreatePrivateChatAsync_NewChat_SendsChatCreatedToOtherUser()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var otherUserName = "Alice";

        _chatRepoMock
            .Setup(r => r.GetPrivateChatAsync(userId, otherUserId))
            .ReturnsAsync((BasicApi.Storage.Entities.Chat?)null);

        BasicApi.Storage.Entities.Chat? capturedChat = null;
        _chatRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<BasicApi.Storage.Entities.Chat>(), It.IsAny<Guid[]>()))
            .Callback<BasicApi.Storage.Entities.Chat, Guid[]>((chat, _) => capturedChat = chat)
            .ReturnsAsync(() => capturedChat!.Id);

        _chatRepoMock
            .Setup(r => r.GetUserNameAsync(otherUserId))
            .ReturnsAsync(otherUserName);

        // Act
        var result = await _handler.CreatePrivateChatAsync(userId, otherUserId);

        // Assert
        Assert.IsType<CreatedResult>(result);

        // Hub: ChatCreated отправлен другому участнику
        _hubClientsMock.Verify(c => c.User(otherUserId.ToString()), Times.Once);
        var inv = Assert.Single(_clientProxy.Invocations);
        Assert.Equal("ChatCreated", inv.Method);
        Assert.Equal(2, inv.Args.Length);

        // Первый аргумент — chatId
        Assert.Equal(capturedChat!.Id, inv.Args[0]);

        // Второй аргумент — ChatCreatedEventDto с информацией о чате
        var payload = Assert.IsType<ChatCreatedEventDto>(inv.Args[1]);
        Assert.Equal("private", payload.Type);
        Assert.Null(payload.Title);
        Assert.Equal(otherUserName, payload.CompanionName);
    }

    [Fact]
    public async Task CreatePrivateChatAsync_ExistingChat_DoesNotSendChatCreated()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var existingChatId = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.GetPrivateChatAsync(userId, otherUserId))
            .ReturnsAsync(new BasicApi.Storage.Entities.Chat { Id = existingChatId });

        // Act
        var result = await _handler.CreatePrivateChatAsync(userId, otherUserId);

        // Assert
        Assert.IsType<OkObjectResult>(result);
        Assert.Empty(_clientProxy.Invocations); // Нет SignalR событий для существующего чата
    }

    [Fact]
    public async Task CreatePrivateChatAsync_NewChat_SendsToCorrectUserOnly()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var otherUserName = "Bob";

        _chatRepoMock
            .Setup(r => r.GetPrivateChatAsync(userId, otherUserId))
            .ReturnsAsync((BasicApi.Storage.Entities.Chat?)null);

        BasicApi.Storage.Entities.Chat? capturedChat = null;
        _chatRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<BasicApi.Storage.Entities.Chat>(), It.IsAny<Guid[]>()))
            .Callback<BasicApi.Storage.Entities.Chat, Guid[]>((chat, _) => capturedChat = chat)
            .ReturnsAsync(() => capturedChat!.Id);

        _chatRepoMock
            .Setup(r => r.GetUserNameAsync(otherUserId))
            .ReturnsAsync(otherUserName);

        // Act
        await _handler.CreatePrivateChatAsync(userId, otherUserId);

        // Assert — событие уходит ТОЛЬКО другому пользователю, НЕ создателю
        _hubClientsMock.Verify(c => c.User(userId.ToString()), Times.Never);
        _hubClientsMock.Verify(c => c.User(otherUserId.ToString()), Times.Once);
    }

    // ========== Search Chats ==========

    [Fact]
    public async Task SearchChatsAsync_GroupType_ReturnsOkWithResults()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var query = "team";
        var response = new SearchChatsResponseDto
        {
            Items =
            [
                new ChatListItemDto
                {
                    ChatId = Guid.NewGuid(),
                    Type = "group",
                    Title = "Team Alpha",
                    UnreadCount = 3,
                    LastActivityAt = DateTime.UtcNow
                }
            ],
            Query = query,
            TotalCount = 1
        };

        _chatServiceMock
            .Setup(s => s.SearchChatsAsync(userId, query, "group", 20))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.SearchChatsAsync(userId, query, "group", 20);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<SearchChatsResponseDto>(okResult.Value);
        Assert.Single(dto.Items);
        Assert.Equal("Team Alpha", dto.Items[0].Title);
        Assert.Equal("group", dto.Items[0].Type);
        Assert.Equal(query, dto.Query);
        Assert.Equal(1, dto.TotalCount);
    }

    [Fact]
    public async Task SearchChatsAsync_PrivateType_ReturnsOkWithResults()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var query = "alice";
        var response = new SearchChatsResponseDto
        {
            Items =
            [
                new ChatListItemDto
                {
                    ChatId = Guid.NewGuid(),
                    Type = "private",
                    CompanionName = "Alice Johnson",
                    UnreadCount = 1,
                    LastActivityAt = DateTime.UtcNow
                }
            ],
            Query = query,
            TotalCount = 1
        };

        _chatServiceMock
            .Setup(s => s.SearchChatsAsync(userId, query, "private", 20))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.SearchChatsAsync(userId, query, "private", 20);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<SearchChatsResponseDto>(okResult.Value);
        Assert.Single(dto.Items);
        Assert.Equal("Alice Johnson", dto.Items[0].CompanionName);
        Assert.Equal("private", dto.Items[0].Type);
        Assert.Equal(query, dto.Query);
    }

    [Fact]
    public async Task SearchChatsAsync_NoType_ReturnsBothGroupAndPrivate()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var query = "test";

        var bothResponse = new SearchChatsResponseDto
        {
            Items =
            [
                new ChatListItemDto { ChatId = Guid.NewGuid(), Type = "group", Title = "Test Group" },
                new ChatListItemDto { ChatId = Guid.NewGuid(), Type = "private", CompanionName = "Test User" }
            ],
            Query = query,
            TotalCount = 2
        };

        _chatServiceMock
            .Setup(s => s.SearchChatsAsync(userId, query, null, 20))
            .ReturnsAsync(bothResponse);

        // Act
        var result = await _handler.SearchChatsAsync(userId, query, null, 20);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<SearchChatsResponseDto>(okResult.Value);
        Assert.Equal(2, dto.Items.Count);
        Assert.Equal("group", dto.Items[0].Type);
        Assert.Equal("private", dto.Items[1].Type);
        Assert.Equal(query, dto.Query);
        Assert.Equal(2, dto.TotalCount);
    }

    [Fact]
    public async Task SearchChatsAsync_EmptyQuery_ThrowsBadRequest()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _chatServiceMock
            .Setup(s => s.SearchChatsAsync(userId, "", "group", 20))
            .ThrowsAsync(new BadRequestException("Query cannot be empty", "INVALID_QUERY"));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            _handler.SearchChatsAsync(userId, "", "group", 20));

        Assert.Contains("empty", ex.Message);
    }
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
