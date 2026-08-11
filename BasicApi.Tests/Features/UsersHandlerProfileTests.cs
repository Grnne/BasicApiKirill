using BasicApi.Features.Users;
using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Users;
using BasicApi.Services;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BasicApi.Tests.Features;

public class UsersHandlerProfileTests
{
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<IChatRepository> _chatRepoMock;
    private readonly Mock<IUserStatusService> _statusServiceMock;
    private readonly UsersHandler _handler;

    public UsersHandlerProfileTests()
    {
        _userRepoMock = new Mock<IUserRepository>();
        _chatRepoMock = new Mock<IChatRepository>();
        _statusServiceMock = new Mock<IUserStatusService>();
        _handler = new UsersHandler(_userRepoMock.Object, _chatRepoMock.Object, _statusServiceMock.Object);
    }

    private static User MakeUser(Guid id) => new()
    {
        Id = id,
        Username = "alice",
        Email = "alice@example.com",
        PasswordHash = "hash",
        DisplayName = "Alice",
        CreatedAt = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
        IsActive = true
    };

    // ===== Own profile (/api/users/me) =====

    [Fact]
    public async Task GetOwnProfileAsync_Found_ReturnsOkWithProfile()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = MakeUser(userId);

        _userRepoMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _handler.GetOwnProfileAsync(userId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<OwnProfileResponseDto>(okResult.Value);
        Assert.Equal(userId, dto.UserId);
        Assert.Equal("alice", dto.Username);
        Assert.Equal("Alice", dto.DisplayName);
    }

    [Fact]
    public async Task GetOwnProfileAsync_IncludesEmail_SoSessionRestoreMatchesLogin()
    {
        // Arrange — a client restoring from a stored token must recover the same
        // user data login/register returned, email included.
        var userId = Guid.NewGuid();

        _userRepoMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeUser(userId));

        // Act
        var result = await _handler.GetOwnProfileAsync(userId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<OwnProfileResponseDto>(okResult.Value);
        Assert.Equal("alice@example.com", dto.Email);
    }

    [Fact]
    public async Task GetOwnProfileAsync_AccountDeleted_ThrowsNotFoundException()
    {
        // Arrange — token still valid, but the user row is gone
        var userId = Guid.NewGuid();

        _userRepoMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            _handler.GetOwnProfileAsync(userId));

        Assert.Equal("USER_NOT_FOUND", ex.ErrorCode);
    }

    // ===== Public profile (/api/users/{userId}) =====

    [Fact]
    public async Task GetUserProfileAsync_Found_ReturnsOkWithProfile()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = MakeUser(userId);

        _userRepoMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act
        var result = await _handler.GetUserProfileAsync(userId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<UserProfileResponseDto>(okResult.Value);
        Assert.Equal(userId, dto.UserId);
        Assert.Equal("alice", dto.Username);
        Assert.Equal("Alice", dto.DisplayName);
    }

    [Fact]
    public async Task GetUserProfileAsync_DoesNotExposeEmailOrPresence()
    {
        // Arrange — another user's email is private, and presence must go through
        // /api/users/status, which is scoped to the caller's own chats.
        var userId = Guid.NewGuid();

        _userRepoMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeUser(userId));

        // Act
        var result = await _handler.GetUserProfileAsync(userId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<UserProfileResponseDto>(okResult.Value);

        Assert.DoesNotContain(
            dto.GetType().GetProperties(),
            p => p.Name is "Email" or "IsOnline" or "LastLoginAt" or "PasswordHash");

        _statusServiceMock.Verify(
            s => s.GetOnlineUserIdsAsync(It.IsAny<IReadOnlySet<Guid>>()), Times.Never);
    }

    [Fact]
    public async Task GetUserProfileAsync_UserNotFound_ThrowsNotFoundException()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userRepoMock
            .Setup(r => r.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            _handler.GetUserProfileAsync(userId));

        Assert.Equal("USER_NOT_FOUND", ex.ErrorCode);
    }
}
