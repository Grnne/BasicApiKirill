using BasicApi.Features.Auth;
using BasicApi.Models.Dto.Auth;
using BasicApi.Services;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BasicApi.Tests.Features;

public class AuthHandlerLogoutValidateTests
{
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<IJwtService> _jwtServiceMock;
    private readonly Mock<ISessionService> _sessionServiceMock;
    private readonly AuthHandler _handler;

    public AuthHandlerLogoutValidateTests()
    {
        _userRepoMock = new Mock<IUserRepository>();
        _jwtServiceMock = new Mock<IJwtService>();
        _sessionServiceMock = new Mock<ISessionService>();

        // Сессии проверяются отдельно в SessionServiceTests; здесь достаточно,
        // чтобы выдача пары повторяла данные пользователя и токены из IJwtService.
        _sessionServiceMock
            .Setup(s => s.IssueForUserAsync(
                It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User u, string? _, string? __, CancellationToken ___) => new AuthResponseDto
            {
                UserId = u.Id,
                Username = u.Username,
                Email = u.Email,
                DisplayName = u.DisplayName,
                Token = _jwtServiceMock.Object.GenerateToken(u.Id, u.Username, u.Email),
                ExpiresAt = _jwtServiceMock.Object.GetExpiryDate(),
                RefreshToken = "refresh-token",
                RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(30)
            });

        _handler = new AuthHandler(_userRepoMock.Object, _jwtServiceMock.Object, _sessionServiceMock.Object);
    }

    // ========== LogoutAsync Tests ==========

    [Fact]
    public async Task LogoutAsync_WithRefreshToken_RevokesThatSession()
    {
        // Act
        var result = await _handler.LogoutAsync("refresh-token");

        // Assert
        Assert.IsType<OkResult>(result);
        _sessionServiceMock.Verify(
            s => s.RevokeAsync("refresh-token", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogoutAsync_WithoutRefreshToken_StillReturnsOk()
    {
        // Arrange — клиент может не прислать токен; отвечать ошибкой не за что,
        // но и гасить тогда нечего.
        // Act
        var result = await _handler.LogoutAsync(null);

        // Assert
        Assert.IsType<OkResult>(result);
        _sessionServiceMock.Verify(
            s => s.RevokeAsync(null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogoutAsync_UnknownToken_ReturnsOk()
    {
        // Arrange — logout идемпотентен и не должен работать оракулом
        // «существует ли такой токен».
        _sessionServiceMock
            .Setup(s => s.RevokeAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.LogoutAsync("never-issued");

        // Assert
        Assert.IsType<OkResult>(result);
    }

    // ========== LogoutAllAsync Tests ==========

    [Fact]
    public async Task LogoutAllAsync_RevokesEverySessionOfTheUser()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        var result = await _handler.LogoutAllAsync(userId);

        // Assert
        Assert.IsType<OkResult>(result);
        _sessionServiceMock.Verify(
            s => s.RevokeAllForUserAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ========== RefreshAsync Tests ==========

    [Fact]
    public async Task RefreshAsync_ValidToken_ReturnsOkWithNewPair()
    {
        // Arrange
        var expected = new AuthResponseDto
        {
            UserId = Guid.NewGuid(),
            Username = "alice",
            Token = "new-access",
            RefreshToken = "new-refresh"
        };

        _sessionServiceMock
            .Setup(s => s.RefreshAsync("old-refresh", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _handler.RefreshAsync("old-refresh");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var dto = Assert.IsType<AuthResponseDto>(okResult.Value);
        Assert.Equal("new-access", dto.Token);
        Assert.Equal("new-refresh", dto.RefreshToken);
    }

    [Fact]
    public async Task RefreshAsync_RejectedToken_PropagatesUnauthorized()
    {
        // Arrange
        _sessionServiceMock
            .Setup(s => s.RefreshAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BasicApi.Middleware.Exceptions.UnauthorizedException(
                "Refresh token has already been used", "REFRESH_TOKEN_REUSED"));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BasicApi.Middleware.Exceptions.UnauthorizedException>(() =>
            _handler.RefreshAsync("stolen"));

        Assert.Equal("REFRESH_TOKEN_REUSED", ex.ErrorCode);
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
