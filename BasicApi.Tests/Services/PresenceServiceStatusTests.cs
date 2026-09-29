using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Users;
using BasicApi.Services;
using BasicApi.Services.Events;
using BasicApi.Storage.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BasicApi.Tests.Services;

public class PresenceServiceStatusTests
{
    private readonly Mock<IChatRepository> _chatRepoMock;
    private readonly Mock<IUserStatusService> _statusServiceMock;
    private readonly PresenceService _service;

    public PresenceServiceStatusTests()
    {
        _chatRepoMock = new Mock<IChatRepository>();
        _statusServiceMock = new Mock<IUserStatusService>();
        _service = new PresenceService(
            _statusServiceMock.Object,
            new MembershipService(_chatRepoMock.Object),
            Mock.Of<IChatEventPublisher>(),
            NullLogger<PresenceService>.Instance);
    }

    // ========== GetOnlineStatusAsync Tests ==========

    [Fact]
    public async Task GetOnlineStatusAsync_WithOnlineMembers_ReturnsOkWithOnlineIds()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var memberA = Guid.NewGuid();
        var memberB = Guid.NewGuid();
        var memberC = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.GetAllChatMembersAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([memberA, memberB, memberC]);

        _statusServiceMock
            .Setup(s => s.GetOnlineUserIdsAsync(It.IsAny<IReadOnlySet<Guid>>()))
            .ReturnsAsync(new HashSet<Guid> { memberA, memberB });

        // Act
        var result = await _service.GetContactsOnlineAsync(userId);

        // Assert
        var dto = result;
        Assert.Equal(2, dto.Items.Count);

        var a = dto.Items.Single(x => x.UserId == memberA);
        Assert.True(a.IsOnline);
        var b = dto.Items.Single(x => x.UserId == memberB);
        Assert.True(b.IsOnline);

        // MemberC not in response because offline
        Assert.DoesNotContain(dto.Items, x => x.UserId == memberC);
    }

    [Fact]
    public async Task GetOnlineStatusAsync_NoMembers_ReturnsEmptyList()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.GetAllChatMembersAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var result = await _service.GetContactsOnlineAsync(userId);

        // Assert
        var dto = result;
        Assert.Empty(dto.Items);
        _statusServiceMock.Verify(s => s.GetOnlineUserIdsAsync(It.IsAny<IReadOnlySet<Guid>>()), Times.Never);
    }

    [Fact]
    public async Task GetOnlineStatusAsync_NoOnlineMembers_ReturnsEmptyList()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var memberA = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.GetAllChatMembersAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([memberA]);

        _statusServiceMock
            .Setup(s => s.GetOnlineUserIdsAsync(It.IsAny<IReadOnlySet<Guid>>()))
            .ReturnsAsync(new HashSet<Guid>());

        // Act
        var result = await _service.GetContactsOnlineAsync(userId);

        // Assert
        var dto = result;
        Assert.Empty(dto.Items);
    }

    // ========== GetTypingStatusAsync Tests ==========

    [Fact]
    public async Task GetTypingStatusAsync_WithTypingUsers_ReturnsOkWithTypingStatus()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var chatA = Guid.NewGuid();
        var chatB = Guid.NewGuid();
        var typerA = Guid.NewGuid();
        var typerB = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.GetUserChatIdsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([chatA, chatB]);

        _statusServiceMock
            .Setup(s => s.GetTypingStatusAsync(It.Is<IReadOnlyCollection<Guid>>(ids =>
                ids.Count == 2 && ids.Contains(chatA) && ids.Contains(chatB))))
            .ReturnsAsync(new Dictionary<Guid, HashSet<Guid>>
            {
                [chatA] = [typerA],
                [chatB] = [typerB]
            });

        // Act
        var result = await _service.GetTypingAsync(userId);

        // Assert
        var dto = result;
        Assert.Equal(2, dto.Items.Count);

        var a = dto.Items.Single(x => x.ChatId == chatA);
        Assert.Equal(typerA, a.UserId);
        Assert.True(a.IsTyping);

        var b = dto.Items.Single(x => x.ChatId == chatB);
        Assert.Equal(typerB, b.UserId);
        Assert.True(b.IsTyping);
    }

    [Fact]
    public async Task GetTypingStatusAsync_FiltersToUserChatsOnly_IgnoresOtherChats()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var userChat = Guid.NewGuid();
        var otherChat = Guid.NewGuid();
        var typer = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.GetUserChatIdsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([userChat]);

        IReadOnlyCollection<Guid>? requested = null;
        _statusServiceMock
            .Setup(s => s.GetTypingStatusAsync(It.IsAny<IReadOnlyCollection<Guid>>()))
            .Callback((IReadOnlyCollection<Guid> ids) => requested = ids)
            .ReturnsAsync(new Dictionary<Guid, HashSet<Guid>> { [userChat] = [typer] });

        // Act
        var result = await _service.GetTypingAsync(userId);

        // Assert — сервис спрашивают только про чаты пользователя, чужие даже не читаются
        Assert.Equal([userChat], requested);
        Assert.DoesNotContain(otherChat, requested!);
        var dto = result;
        Assert.Single(dto.Items);
        Assert.Equal(userChat, dto.Items[0].ChatId);
        Assert.Equal(typer, dto.Items[0].UserId);
    }

    [Fact]
    public async Task GetTypingStatusAsync_NoTyping_ReturnsEmptyList()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _statusServiceMock
            .Setup(s => s.GetTypingStatusAsync(It.IsAny<IReadOnlyCollection<Guid>>()))
            .ReturnsAsync(new Dictionary<Guid, HashSet<Guid>>());

        // Act
        var result = await _service.GetTypingAsync(userId);

        // Assert
        var dto = result;
        Assert.Empty(dto.Items);
    }

    // ========== GetUserStatusAsync (точечный статус одного пользователя) ==========

    [Fact]
    public async Task GetUserStatusAsync_SharedChatAndOnline_ReturnsOnlineTrue()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var target = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.GetAllChatMembersAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([target]);

        _statusServiceMock
            .Setup(s => s.GetOnlineUserIdsAsync(It.IsAny<IReadOnlySet<Guid>>()))
            .ReturnsAsync(new HashSet<Guid> { target });

        // Act
        var result = await _service.GetUserStatusAsync(userId, target);

        // Assert
        var dto = result;
        Assert.Equal(target, dto.UserId);
        Assert.True(dto.IsOnline);
    }

    [Fact]
    public async Task GetUserStatusAsync_SharedChatAndOffline_ReturnsOnlineFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var target = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.GetAllChatMembersAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([target]);

        _statusServiceMock
            .Setup(s => s.GetOnlineUserIdsAsync(It.IsAny<IReadOnlySet<Guid>>()))
            .ReturnsAsync(new HashSet<Guid>());

        // Act
        var result = await _service.GetUserStatusAsync(userId, target);

        // Assert — оффлайн отдаётся явным false, а не отсутствием записи
        var dto = result;
        Assert.Equal(target, dto.UserId);
        Assert.False(dto.IsOnline);
    }

    [Fact]
    public async Task GetUserStatusAsync_Self_ReturnsOwnStatus()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.GetAllChatMembersAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _statusServiceMock
            .Setup(s => s.GetOnlineUserIdsAsync(It.IsAny<IReadOnlySet<Guid>>()))
            .ReturnsAsync(new HashSet<Guid> { userId });

        // Act
        var result = await _service.GetUserStatusAsync(userId, userId);

        // Assert
        var dto = result;
        Assert.True(dto.IsOnline);
    }

    [Fact]
    public async Task GetUserStatusAsync_NoSharedChat_ThrowsNotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var stranger = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.GetAllChatMembersAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Guid.NewGuid()]);

        // Act & Assert — статус видно только по участникам общих чатов
        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetUserStatusAsync(userId, stranger));

        Assert.Equal("USER_NOT_FOUND", ex.ErrorCode);
        _statusServiceMock.Verify(s => s.GetOnlineUserIdsAsync(It.IsAny<IReadOnlySet<Guid>>()), Times.Never);
    }

    // ========== GetUsersStatusAsync (батч по списку id) ==========

    [Fact]
    public async Task GetUsersStatusAsync_ReturnsBothOnlineAndOfflineForRequestedIds()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var onlineMember = Guid.NewGuid();
        var offlineMember = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.GetAllChatMembersAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([onlineMember, offlineMember]);

        _statusServiceMock
            .Setup(s => s.GetOnlineUserIdsAsync(It.IsAny<IReadOnlySet<Guid>>()))
            .ReturnsAsync(new HashSet<Guid> { onlineMember });

        // Act
        var result = await _service.GetUsersStatusAsync(userId, [onlineMember, offlineMember]);

        // Assert
        var dto = result;
        Assert.Equal(2, dto.Items.Count);
        Assert.True(dto.Items.Single(x => x.UserId == onlineMember).IsOnline);
        Assert.False(dto.Items.Single(x => x.UserId == offlineMember).IsOnline);
    }

    [Fact]
    public async Task GetUsersStatusAsync_FiltersOutUsersWithoutSharedChat()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var member = Guid.NewGuid();
        var stranger = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.GetAllChatMembersAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([member]);

        _statusServiceMock
            .Setup(s => s.GetOnlineUserIdsAsync(It.IsAny<IReadOnlySet<Guid>>()))
            .ReturnsAsync(new HashSet<Guid> { member });

        // Act
        var result = await _service.GetUsersStatusAsync(userId, [member, stranger]);

        // Assert — чужие id молча выпадают из ответа
        var dto = result;
        Assert.Equal(member, Assert.Single(dto.Items).UserId);
    }

    [Fact]
    public async Task GetUsersStatusAsync_DeduplicatesRequestedIds()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var member = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.GetAllChatMembersAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([member]);

        _statusServiceMock
            .Setup(s => s.GetOnlineUserIdsAsync(It.IsAny<IReadOnlySet<Guid>>()))
            .ReturnsAsync(new HashSet<Guid> { member });

        // Act
        var result = await _service.GetUsersStatusAsync(userId, [member, member, member]);

        // Assert
        var dto = result;
        Assert.Single(dto.Items);
    }

    [Fact]
    public async Task GetUsersStatusAsync_EmptyList_ThrowsBadRequest()
    {
        // Act & Assert
        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            _service.GetUsersStatusAsync(Guid.NewGuid(), []));

        Assert.Equal("INVALID_REQUEST", ex.ErrorCode);
    }

    [Fact]
    public async Task GetUsersStatusAsync_TooManyIds_ThrowsBadRequest()
    {
        // Arrange — лимит батча 200 id
        var ids = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToList();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            _service.GetUsersStatusAsync(Guid.NewGuid(), ids));

        Assert.Equal("TOO_MANY_IDS", ex.ErrorCode);
    }
}
