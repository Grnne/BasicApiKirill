using BasicApi.Features.Auth;
using BasicApi.Services;
using BasicApi.Storage.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BasicApi.Tests.Features;

public class AuthHandlerLogoutValidateTests
{
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<IJwtService> _jwtServiceMock;
    private readonly AuthHandler _handler;

    public AuthHandlerLogoutValidateTests()
    {
        _userRepoMock = new Mock<IUserRepository>();
        _jwtServiceMock = new Mock<IJwtService>();
        _handler = new AuthHandler(_userRepoMock.Object, _jwtServiceMock.Object);
    }

    // ========== LogoutAsync Tests ==========

    [Fact]
    public async Task LogoutAsync_ReturnsOk()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        var result = await _handler.LogoutAsync(userId);

        // Assert
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task LogoutAsync_WithAnyUserId_ReturnsOk()
    {
        // Act
        var result = await _handler.LogoutAsync(Guid.NewGuid());

        // Assert
        Assert.IsType<OkResult>(result);
    }

    // ========== ValidateTokenAsync Tests ==========

    [Fact]
    public async Task ValidateTokenAsync_ValidToken_ReturnsOkWithUserInfo()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var username = "testuser";

        _jwtServiceMock
            .Setup(s => s.TryValidateToken(It.IsAny<string>(), out userId, out username))
            .Returns(true);

        // Act
        var result = await _handler.ValidateTokenAsync("some-valid-token");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<BasicApi.Models.Dto.Auth.ValidateTokenResponseDto>(okResult.Value);
        Assert.True(dto.IsValid);
        Assert.Equal(userId, dto.UserId);
        Assert.Equal(username, dto.Username);
    }

    [Fact]
    public async Task ValidateTokenAsync_InvalidToken_ReturnsOkWithIsValidFalse()
    {
        // Arrange
        var userId = Guid.Empty;
        var username = string.Empty;

        _jwtServiceMock
            .Setup(s => s.TryValidateToken(It.IsAny<string>(), out userId, out username))
            .Returns(false);

        // Act
        var result = await _handler.ValidateTokenAsync("invalid-token");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<BasicApi.Models.Dto.Auth.ValidateTokenResponseDto>(okResult.Value);
        Assert.False(dto.IsValid);
    }
}
