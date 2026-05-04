using BasicApi.Features.Users;
using BasicApi.Middleware.Exceptions;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BasicApi.Tests.Features;

public class UsersHandlerTests
{
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly UsersHandler _handler;

    public UsersHandlerTests()
    {
        _userRepoMock = new Mock<IUserRepository>();
        _handler = new UsersHandler(_userRepoMock.Object);
    }

    [Fact]
    public async Task GetUserIdAsync_Found_ReturnsOkWithUserId()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _userRepoMock
            .Setup(r => r.GetIdByUsernameOrEmailAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(userId);

        // Act
        var result = await _handler.GetUserIdAsync("testuser");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<BasicApi.Models.Dto.Users.UserIdResponseDto>(okResult.Value);
        Assert.Equal(userId, dto.UserId);
    }

    [Fact]
    public async Task GetUserIdAsync_UserNotFound_ThrowsNotFoundException()
    {
        // Arrange
        _userRepoMock
            .Setup(r => r.GetIdByUsernameOrEmailAsync("unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            _handler.GetUserIdAsync("unknown"));

        Assert.Contains("User not found", ex.Message);
    }

    [Fact]
    public async Task GetUserIdAsync_EmptyGuid_ThrowsNotFoundException()
    {
        // Arrange
        _userRepoMock
            .Setup(r => r.GetIdByUsernameOrEmailAsync("empty", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.Empty);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _handler.GetUserIdAsync("empty"));
    }

    // ========== SearchUsersAsync Tests ==========

    [Fact]
    public async Task SearchUsersAsync_ReturnsMappedResults()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var query = "alice";
        var users = new List<User>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Username = "alice123",
                DisplayName = "Alice Johnson"
            },
            new()
            {
                Id = Guid.NewGuid(),
                Username = "alice_smith",
                DisplayName = "Alice Smith"
            }
        };

        _userRepoMock
            .Setup(r => r.SearchByDisplayNameOrUsernameAsync(query, userId, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(users);

        _userRepoMock
            .Setup(r => r.CountBySearchQueryAsync(query, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        // Act
        var result = await _handler.SearchUsersAsync(userId, query, 20);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<BasicApi.Models.Dto.Users.SearchUsersResponseDto>(okResult.Value);
        Assert.Equal(2, dto.Items.Count);
        Assert.Equal(users[0].Id, dto.Items[0].UserId);
        Assert.Equal(users[0].Username, dto.Items[0].Username);
        Assert.Equal(users[0].DisplayName, dto.Items[0].DisplayName);
        Assert.Equal(users[1].Id, dto.Items[1].UserId);
        Assert.Equal(users[1].Username, dto.Items[1].Username);
        Assert.Equal(users[1].DisplayName, dto.Items[1].DisplayName);
        Assert.Equal(query, dto.Query);
        Assert.Equal(2, dto.TotalCount);
    }

    [Fact]
    public async Task SearchUsersAsync_EmptyResults_ReturnsEmptyList()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var query = "nonexistent";

        _userRepoMock
            .Setup(r => r.SearchByDisplayNameOrUsernameAsync(query, userId, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _userRepoMock
            .Setup(r => r.CountBySearchQueryAsync(query, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Act
        var result = await _handler.SearchUsersAsync(userId, query, 20);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<BasicApi.Models.Dto.Users.SearchUsersResponseDto>(okResult.Value);
        Assert.Empty(dto.Items);
        Assert.Equal(query, dto.Query);
        Assert.Equal(0, dto.TotalCount);
    }

    [Fact]
    public async Task SearchUsersAsync_EmptyQuery_ThrowsBadRequest()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            _handler.SearchUsersAsync(userId, "", 20));

        Assert.Contains("empty", ex.Message);
    }

    [Fact]
    public async Task SearchUsersAsync_WhitespaceQuery_ThrowsBadRequest()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            _handler.SearchUsersAsync(userId, "   ", 20));

        Assert.Contains("empty", ex.Message);
    }

    [Fact]
    public async Task SearchUsersAsync_WithLimit_RespectsLimit()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var query = "test";
        var users = new List<User>
        {
            new() { Id = Guid.NewGuid(), Username = "user1", DisplayName = "Test User 1" }
        };

        _userRepoMock
            .Setup(r => r.SearchByDisplayNameOrUsernameAsync(query, userId, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(users);

        _userRepoMock
            .Setup(r => r.CountBySearchQueryAsync(query, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.SearchUsersAsync(userId, query, 5);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<BasicApi.Models.Dto.Users.SearchUsersResponseDto>(okResult.Value);
        Assert.Single(dto.Items);
        Assert.Equal(1, dto.TotalCount);

        _userRepoMock.Verify(r => r.SearchByDisplayNameOrUsernameAsync(query, userId, 5, It.IsAny<CancellationToken>()), Times.Once);
    }
}
