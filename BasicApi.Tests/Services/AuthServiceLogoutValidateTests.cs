using BasicApi.Models.Dto.Auth;
using BasicApi.Services;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Moq;

namespace BasicApi.Tests.Services;

public class AuthServiceLogoutValidateTests
{
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<IJwtService> _jwtServiceMock;
    private readonly Mock<ISessionService> _sessionServiceMock;
    private readonly AuthService _service;

    public AuthServiceLogoutValidateTests()
    {
        _userRepoMock = new Mock<IUserRepository>();
        _jwtServiceMock = new Mock<IJwtService>();
        _sessionServiceMock = new Mock<ISessionService>();

        // Sessions are checked separately in SessionServiceTests; here it is enough that
        // the issued pair repeats the user data and tokens from IJwtService.
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

        _service = new AuthService(_userRepoMock.Object, _jwtServiceMock.Object, _sessionServiceMock.Object,
            new BasicApi.Hubs.HubConnectionRegistry());
    }

    // ========== LogoutAsync Tests ==========

    [Fact]
    public async Task LogoutAsync_WithRefreshToken_RevokesThatSession()
    {
        // Act
        await _service.LogoutAsync("refresh-token");

        // Assert
        _sessionServiceMock.Verify(
            s => s.RevokeAsync("refresh-token", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogoutAsync_WithoutRefreshToken_StillReturnsOk()
    {
        // Arrange - the client may not send a token; there is nothing to fail with an error,
        // but nothing to revoke either.
        // Act
        await _service.LogoutAsync(null);

        // Assert
        _sessionServiceMock.Verify(
            s => s.RevokeAsync(null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogoutAsync_UnknownToken_ReturnsOk()
    {
        // Arrange - logout is idempotent and must not work as an oracle for
        // "does such a token exist".
        _sessionServiceMock
            .Setup(s => s.RevokeAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        // Act
        await _service.LogoutAsync("never-issued");

        // Assert
    }

    // ========== LogoutAllAsync Tests ==========

    [Fact]
    public async Task LogoutAllAsync_RevokesEverySessionOfTheUser()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        await _service.LogoutAllAsync(userId);

        // Assert
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
        var result = await _service.RefreshAsync("old-refresh");

        // Assert
        var dto = result;
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
            _service.RefreshAsync("stolen"));

        Assert.Equal("REFRESH_TOKEN_REUSED", ex.ErrorCode);
    }

    // ========== ValidateTokenAsync Tests ==========

    [Fact]
    public void ValidateToken_ValidToken_ReturnsOkWithUserInfo()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var username = "testuser";

        _jwtServiceMock
            .Setup(s => s.TryValidateToken(It.IsAny<string>(), out userId, out username))
            .Returns(true);

        // Act
        var result = _service.ValidateToken("some-valid-token");

        // Assert
        var dto = result;
        Assert.True(dto.IsValid);
        Assert.Equal(userId, dto.UserId);
        Assert.Equal(username, dto.Username);
    }

    [Fact]
    public void ValidateToken_InvalidToken_ReturnsOkWithIsValidFalse()
    {
        // Arrange
        var userId = Guid.Empty;
        var username = string.Empty;

        _jwtServiceMock
            .Setup(s => s.TryValidateToken(It.IsAny<string>(), out userId, out username))
            .Returns(false);

        // Act
        var result = _service.ValidateToken("invalid-token");

        // Assert
        var dto = result;
        Assert.False(dto.IsValid);
    }
}
