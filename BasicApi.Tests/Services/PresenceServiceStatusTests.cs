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
            new ChatPolicy(new MembershipService(_chatRepoMock.Object)),
            Mock.Of<IChatEventPublisher>(),
            NullLogger<PresenceService>.Instance);
    }

    [Fact]
    public async Task GetOnlineStatusAsync_WithOnlineMembers_ReturnsOkWithOnlineIds()
    {
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

        var result = await _service.GetContactsOnlineAsync(userId);

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
        var userId = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.GetAllChatMembersAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await _service.GetContactsOnlineAsync(userId);

        var dto = result;
        Assert.Empty(dto.Items);
        _statusServiceMock.Verify(s => s.GetOnlineUserIdsAsync(It.IsAny<IReadOnlySet<Guid>>()), Times.Never);
    }

    [Fact]
    public async Task GetOnlineStatusAsync_NoOnlineMembers_ReturnsEmptyList()
    {
        var userId = Guid.NewGuid();
        var memberA = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.GetAllChatMembersAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([memberA]);

        _statusServiceMock
            .Setup(s => s.GetOnlineUserIdsAsync(It.IsAny<IReadOnlySet<Guid>>()))
            .ReturnsAsync(new HashSet<Guid>());

        var result = await _service.GetContactsOnlineAsync(userId);

        var dto = result;
        Assert.Empty(dto.Items);
    }

    [Fact]
    public async Task GetTypingStatusAsync_WithTypingUsers_ReturnsOkWithTypingStatus()
    {
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

        var result = await _service.GetTypingAsync(userId);

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

        var result = await _service.GetTypingAsync(userId);

        // The service is asked only about the user's chats, others' are not even read
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
        var userId = Guid.NewGuid();

        _statusServiceMock
            .Setup(s => s.GetTypingStatusAsync(It.IsAny<IReadOnlyCollection<Guid>>()))
            .ReturnsAsync(new Dictionary<Guid, HashSet<Guid>>());

        var result = await _service.GetTypingAsync(userId);

        var dto = result;
        Assert.Empty(dto.Items);
    }

    [Fact]
    public async Task GetUserStatusAsync_SharedChatAndOnline_ReturnsOnlineTrue()
    {
        var userId = Guid.NewGuid();
        var target = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.GetAllChatMembersAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([target]);

        _statusServiceMock
            .Setup(s => s.GetOnlineUserIdsAsync(It.IsAny<IReadOnlySet<Guid>>()))
            .ReturnsAsync(new HashSet<Guid> { target });

        var result = await _service.GetUserStatusAsync(userId, target);

        var dto = result;
        Assert.Equal(target, dto.UserId);
        Assert.True(dto.IsOnline);
    }

    [Fact]
    public async Task GetUserStatusAsync_SharedChatAndOffline_ReturnsOnlineFalse()
    {
        var userId = Guid.NewGuid();
        var target = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.GetAllChatMembersAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([target]);

        _statusServiceMock
            .Setup(s => s.GetOnlineUserIdsAsync(It.IsAny<IReadOnlySet<Guid>>()))
            .ReturnsAsync(new HashSet<Guid>());

        var result = await _service.GetUserStatusAsync(userId, target);

        // Offline is returned as an explicit false, not as a missing entry
        var dto = result;
        Assert.Equal(target, dto.UserId);
        Assert.False(dto.IsOnline);
    }

    [Fact]
    public async Task GetUserStatusAsync_Self_ReturnsOwnStatus()
    {
        var userId = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.GetAllChatMembersAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _statusServiceMock
            .Setup(s => s.GetOnlineUserIdsAsync(It.IsAny<IReadOnlySet<Guid>>()))
            .ReturnsAsync(new HashSet<Guid> { userId });

        var result = await _service.GetUserStatusAsync(userId, userId);

        var dto = result;
        Assert.True(dto.IsOnline);
    }

    [Fact]
    public async Task GetUserStatusAsync_NoSharedChat_ThrowsNotFound()
    {
        var userId = Guid.NewGuid();
        var stranger = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.GetAllChatMembersAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Guid.NewGuid()]);

        // The status is visible only to members of shared chats
        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetUserStatusAsync(userId, stranger));

        Assert.Equal("USER_NOT_FOUND", ex.ErrorCode);
        _statusServiceMock.Verify(s => s.GetOnlineUserIdsAsync(It.IsAny<IReadOnlySet<Guid>>()), Times.Never);
    }

    [Fact]
    public async Task GetUsersStatusAsync_ReturnsBothOnlineAndOfflineForRequestedIds()
    {
        var userId = Guid.NewGuid();
        var onlineMember = Guid.NewGuid();
        var offlineMember = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.GetAllChatMembersAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([onlineMember, offlineMember]);

        _statusServiceMock
            .Setup(s => s.GetOnlineUserIdsAsync(It.IsAny<IReadOnlySet<Guid>>()))
            .ReturnsAsync(new HashSet<Guid> { onlineMember });

        var result = await _service.GetUsersStatusAsync(userId, [onlineMember, offlineMember]);

        var dto = result;
        Assert.Equal(2, dto.Items.Count);
        Assert.True(dto.Items.Single(x => x.UserId == onlineMember).IsOnline);
        Assert.False(dto.Items.Single(x => x.UserId == offlineMember).IsOnline);
    }

    [Fact]
    public async Task GetUsersStatusAsync_FiltersOutUsersWithoutSharedChat()
    {
        var userId = Guid.NewGuid();
        var member = Guid.NewGuid();
        var stranger = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.GetAllChatMembersAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([member]);

        _statusServiceMock
            .Setup(s => s.GetOnlineUserIdsAsync(It.IsAny<IReadOnlySet<Guid>>()))
            .ReturnsAsync(new HashSet<Guid> { member });

        var result = await _service.GetUsersStatusAsync(userId, [member, stranger]);

        // Ids of others silently drop out of the response
        var dto = result;
        Assert.Equal(member, Assert.Single(dto.Items).UserId);
    }

    [Fact]
    public async Task GetUsersStatusAsync_DeduplicatesRequestedIds()
    {
        var userId = Guid.NewGuid();
        var member = Guid.NewGuid();

        _chatRepoMock
            .Setup(r => r.GetAllChatMembersAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([member]);

        _statusServiceMock
            .Setup(s => s.GetOnlineUserIdsAsync(It.IsAny<IReadOnlySet<Guid>>()))
            .ReturnsAsync(new HashSet<Guid> { member });

        var result = await _service.GetUsersStatusAsync(userId, [member, member, member]);

        var dto = result;
        Assert.Single(dto.Items);
    }

    [Fact]
    public async Task GetUsersStatusAsync_EmptyList_ThrowsBadRequest()
    {
        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            _service.GetUsersStatusAsync(Guid.NewGuid(), []));

        Assert.Equal("INVALID_REQUEST", ex.ErrorCode);
    }

    [Fact]
    public async Task GetUsersStatusAsync_TooManyIds_ThrowsBadRequest()
    {
        // The batch limit is 200 ids
        var ids = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToList();

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            _service.GetUsersStatusAsync(Guid.NewGuid(), ids));

        Assert.Equal("TOO_MANY_IDS", ex.ErrorCode);
    }
}
