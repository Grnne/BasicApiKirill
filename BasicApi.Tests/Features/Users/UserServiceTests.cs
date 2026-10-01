using BasicApi.Features.Users;
using BasicApi.Middleware.Exceptions;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Moq;

namespace BasicApi.Tests.Features.Users;

public class UserServiceTests
{
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly UserService _service;

    public UserServiceTests()
    {
        _userRepoMock = new Mock<IUserRepository>();
        _service = new UserService(_userRepoMock.Object);
    }

    [Fact]
    public async Task GetUserIdAsync_Found_ReturnsOkWithUserId()
    {
        var userId = Guid.NewGuid();

        _userRepoMock
            .Setup(r => r.GetIdByUsernameAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(userId);

        var result = await _service.GetUserIdAsync("testuser");

        var dto = result;
        Assert.Equal(userId, dto.UserId);
    }

    [Fact]
    public async Task GetUserIdAsync_UserNotFound_ThrowsNotFoundException()
    {
        _userRepoMock
            .Setup(r => r.GetIdByUsernameAsync("unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetUserIdAsync("unknown"));

        Assert.Contains("User not found", ex.Message);
    }

    [Fact]
    public async Task SearchUsersAsync_ReturnsMappedResults()
    {
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

        var result = await _service.SearchUsersAsync(userId, query, 20);

        var dto = result;
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
        var userId = Guid.NewGuid();
        var query = "nonexistent";

        _userRepoMock
            .Setup(r => r.SearchByDisplayNameOrUsernameAsync(query, userId, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _userRepoMock
            .Setup(r => r.CountBySearchQueryAsync(query, userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await _service.SearchUsersAsync(userId, query, 20);

        var dto = result;
        Assert.Empty(dto.Items);
        Assert.Equal(query, dto.Query);
        Assert.Equal(0, dto.TotalCount);
    }

    [Fact]
    public async Task SearchUsersAsync_EmptyQuery_ThrowsBadRequest()
    {
        var userId = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            _service.SearchUsersAsync(userId, "", 20));

        Assert.Contains("empty", ex.Message);
    }

    [Fact]
    public async Task SearchUsersAsync_WhitespaceQuery_ThrowsBadRequest()
    {
        var userId = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            _service.SearchUsersAsync(userId, "   ", 20));

        Assert.Contains("empty", ex.Message);
    }

    [Fact]
    public async Task SearchUsersAsync_WithLimit_RespectsLimit()
    {
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

        var result = await _service.SearchUsersAsync(userId, query, 5);

        var dto = result;
        Assert.Single(dto.Items);
        Assert.Equal(1, dto.TotalCount);

        _userRepoMock.Verify(r => r.SearchByDisplayNameOrUsernameAsync(query, userId, 5, It.IsAny<CancellationToken>()), Times.Once);
    }
}
