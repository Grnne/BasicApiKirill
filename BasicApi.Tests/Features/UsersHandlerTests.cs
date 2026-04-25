using BasicApi.Features.Users;
using BasicApi.Middleware;
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
}
