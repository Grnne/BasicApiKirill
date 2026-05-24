using BasicApi.Features.Users;
using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Users;
using BasicApi.Services;
using BasicApi.Storage.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BasicApi.Tests.Features;

public class UsersHandlerStatusTests
{
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<IChatRepository> _chatRepoMock;
    private readonly Mock<IUserStatusService> _statusServiceMock;
    private readonly UsersHandler _handler;

    public UsersHandlerStatusTests()
    {
        _userRepoMock = new Mock<IUserRepository>();
        _chatRepoMock = new Mock<IChatRepository>();
        _statusServiceMock = new Mock<IUserStatusService>();
        _handler = new UsersHandler(
            _userRepoMock.Object,
            _chatRepoMock.Object,
            _statusServiceMock.Object);
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
            .Setup(r => r.GetAllChatMembersAsync(userId))
            .ReturnsAsync([memberA, memberB, memberC]);

        _statusServiceMock
            .Setup(s => s.GetOnlineUserIdsAsync(It.IsAny<IReadOnlySet<Guid>>()))
            .ReturnsAsync(new HashSet<Guid> { memberA, memberB });

        // Act
        var result = await _handler.GetOnlineStatusAsync(userId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<UserStatusResponseDto>(okResult.Value);
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
            .Setup(r => r.GetAllChatMembersAsync(userId))
            .ReturnsAsync([]);

        // Act
        var result = await _handler.GetOnlineStatusAsync(userId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<UserStatusResponseDto>(okResult.Value);
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
            .Setup(r => r.GetAllChatMembersAsync(userId))
            .ReturnsAsync([memberA]);

        _statusServiceMock
            .Setup(s => s.GetOnlineUserIdsAsync(It.IsAny<IReadOnlySet<Guid>>()))
            .ReturnsAsync(new HashSet<Guid>());

        // Act
        var result = await _handler.GetOnlineStatusAsync(userId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<UserStatusResponseDto>(okResult.Value);
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
            .Setup(r => r.GetUserChatsAsync(userId))
            .ReturnsAsync([new BasicApi.Storage.Entities.Chat { Id = chatA }, new BasicApi.Storage.Entities.Chat { Id = chatB }]);

        _statusServiceMock
            .Setup(s => s.GetTypingStatusAsync(userId))
            .ReturnsAsync(new Dictionary<Guid, HashSet<Guid>>
            {
                [chatA] = [typerA],
                [chatB] = [typerB]
            });

        // Act
        var result = await _handler.GetTypingStatusAsync(userId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<TypingStatusResponseDto>(okResult.Value);
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
            .Setup(r => r.GetUserChatsAsync(userId))
            .ReturnsAsync([new BasicApi.Storage.Entities.Chat { Id = userChat }]);

        _statusServiceMock
            .Setup(s => s.GetTypingStatusAsync(userId))
            .ReturnsAsync(new Dictionary<Guid, HashSet<Guid>>
            {
                [userChat] = [typer],
                [otherChat] = [Guid.NewGuid()] // user is NOT a member of otherChat
            });

        // Act
        var result = await _handler.GetTypingStatusAsync(userId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<TypingStatusResponseDto>(okResult.Value);
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
            .Setup(s => s.GetTypingStatusAsync(userId))
            .ReturnsAsync(new Dictionary<Guid, HashSet<Guid>>());

        // Act
        var result = await _handler.GetTypingStatusAsync(userId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<TypingStatusResponseDto>(okResult.Value);
        Assert.Empty(dto.Items);
    }
}
