using BasicApi.Features.Auth;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Moq;

namespace BasicApi.Tests.Features.Auth;

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

        _service = new AuthService(_userRepoMock.Object, _jwtServiceMock.Object, _sessionServiceMock.Object, Mock.Of<IDeviceRepository>(),
            new BasicApi.Hubs.HubConnectionRegistry());
    }

    [Fact]
    public async Task LogoutAsync_WithRefreshToken_RevokesThatSession()
    {
        await _service.LogoutAsync("refresh-token");

        _sessionServiceMock.Verify(
            s => s.RevokeAsync("refresh-token", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogoutAsync_WithoutRefreshToken_StillReturnsOk()
    {
        // The client may not send a token; there is nothing to fail with an error,
        // but nothing to revoke either.
        await _service.LogoutAsync(null);

        _sessionServiceMock.Verify(
            s => s.RevokeAsync(null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogoutAsync_UnknownToken_ReturnsOk()
    {
        // Logout is idempotent and must not work as an oracle for
        // "does such a token exist".
        _sessionServiceMock
            .Setup(s => s.RevokeAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        await _service.LogoutAsync("never-issued");
    }

    [Fact]
    public async Task LogoutAllAsync_RevokesEverySessionOfTheUser()
    {
        var userId = Guid.NewGuid();

        await _service.LogoutAllAsync(userId);

        _sessionServiceMock.Verify(
            s => s.RevokeAllForUserAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshAsync_ValidToken_ReturnsOkWithNewPair()
    {
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

        var result = await _service.RefreshAsync("old-refresh");

        var dto = result;
        Assert.Equal("new-access", dto.Token);
        Assert.Equal("new-refresh", dto.RefreshToken);
    }

    [Fact]
    public async Task RefreshAsync_RejectedToken_PropagatesUnauthorized()
    {
        _sessionServiceMock
            .Setup(s => s.RefreshAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BasicApi.Middleware.Exceptions.UnauthorizedException(
                "Refresh token has already been used", "REFRESH_TOKEN_REUSED"));

        var ex = await Assert.ThrowsAsync<BasicApi.Middleware.Exceptions.UnauthorizedException>(() =>
            _service.RefreshAsync("stolen"));

        Assert.Equal("REFRESH_TOKEN_REUSED", ex.ErrorCode);
    }

    [Fact]
    public void ValidateToken_ValidToken_ReturnsOkWithUserInfo()
    {
        var userId = Guid.NewGuid();
        var username = "testuser";

        _jwtServiceMock
            .Setup(s => s.TryValidateToken(It.IsAny<string>(), out userId, out username))
            .Returns(true);

        var result = _service.ValidateToken("some-valid-token");

        var dto = result;
        Assert.True(dto.IsValid);
        Assert.Equal(userId, dto.UserId);
        Assert.Equal(username, dto.Username);
    }

    [Fact]
    public void ValidateToken_InvalidToken_ReturnsOkWithIsValidFalse()
    {
        var userId = Guid.Empty;
        var username = string.Empty;

        _jwtServiceMock
            .Setup(s => s.TryValidateToken(It.IsAny<string>(), out userId, out username))
            .Returns(false);

        var result = _service.ValidateToken("invalid-token");

        var dto = result;
        Assert.False(dto.IsValid);
    }
}
