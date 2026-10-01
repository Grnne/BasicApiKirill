using BasicApi.Features.Auth;
using BasicApi.Middleware.Exceptions;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Microsoft.Extensions.Configuration;
using Moq;

namespace BasicApi.Tests.Features.Auth;

/// <summary>Refresh-token sessions: issuing, rotation, the grace window for racing clients, and reuse detection.</summary>
public class SessionServiceTests
{
    private const int GraceSeconds = 30;

    private readonly Mock<ISessionRepository> _sessionRepoMock;
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<IJwtService> _jwtMock;
    private readonly SessionService _service;

    private readonly User _user = new()
    {
        Id = Guid.NewGuid(),
        Username = "alice",
        Email = "alice@test.com",
        DisplayName = "Alice",
        IsActive = true
    };

    public SessionServiceTests()
    {
        _sessionRepoMock = new Mock<ISessionRepository>();
        _userRepoMock = new Mock<IUserRepository>();
        _jwtMock = new Mock<IJwtService>();

        _jwtMock.Setup(j => j.GenerateToken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>()))
            .Returns("access-token");
        _jwtMock.Setup(j => j.GetExpiryDate()).Returns(DateTime.UtcNow.AddMinutes(15));

        _userRepoMock.Setup(r => r.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_user);

        _sessionRepoMock
            .Setup(r => r.TryRotateAsync(It.IsAny<Guid>(), It.IsAny<Session>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _sessionRepoMock
            .Setup(r => r.HasLiveSessionInFamilyAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:RefreshTokenDays"] = "30",
                ["Jwt:RefreshGraceSeconds"] = GraceSeconds.ToString()
            })
            .Build();

        _service = new SessionService(
            _sessionRepoMock.Object, _userRepoMock.Object, _jwtMock.Object, config);
    }

    private Session ActiveSession(string refreshToken, Guid? familyId = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = _user.Id,
        FamilyId = familyId ?? Guid.NewGuid(),
        RefreshTokenHash = SessionService.HashRefreshToken(refreshToken),
        CreatedAt = DateTime.UtcNow.AddMinutes(-5),
        ExpiresAt = DateTime.UtcNow.AddDays(30)
    };

    [Fact]
    public async Task IssueForUserAsync_ReturnsBothTokens()
    {
        var result = await _service.IssueForUserAsync(_user, "android", "1.2.3.4");

        Assert.Equal("access-token", result.Token);
        Assert.False(string.IsNullOrWhiteSpace(result.RefreshToken));
        Assert.True(result.RefreshTokenExpiresAt > DateTime.UtcNow.AddDays(29));
        Assert.Equal(_user.Id, result.UserId);
        Assert.Equal("alice", result.Username);
    }

    [Fact]
    public async Task IssueForUserAsync_StoresOnlyTheHashOfTheRefreshToken()
    {
        Session? stored = null;
        _sessionRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<Session>(), It.IsAny<CancellationToken>()))
            .Callback<Session, CancellationToken>((s, _) => stored = s)
            .Returns(Task.CompletedTask);

        var result = await _service.IssueForUserAsync(_user, "android", "1.2.3.4");

        // The plaintext token must not end up in the DB
        Assert.NotNull(stored);
        Assert.NotEqual(result.RefreshToken, stored!.RefreshTokenHash);
        Assert.Equal(SessionService.HashRefreshToken(result.RefreshToken), stored.RefreshTokenHash);
        Assert.Equal(64, stored.RefreshTokenHash.Length); // sha256 hex
    }

    [Fact]
    public async Task IssueForUserAsync_TwoLoginsProduceDifferentTokensAndFamilies()
    {
        var stored = new List<Session>();
        _sessionRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<Session>(), It.IsAny<CancellationToken>()))
            .Callback<Session, CancellationToken>((s, _) => stored.Add(s))
            .Returns(Task.CompletedTask);

        var first = await _service.IssueForUserAsync(_user, "android", null);
        var second = await _service.IssueForUserAsync(_user, "ios", null);

        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.NotEqual(stored[0].FamilyId, stored[1].FamilyId);
    }

    [Fact]
    public async Task RefreshAsync_ValidToken_IssuesNewPair()
    {
        const string token = "refresh-token-1";
        var session = ActiveSession(token);
        _sessionRepoMock
            .Setup(r => r.GetByRefreshTokenHashAsync(SessionService.HashRefreshToken(token), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var result = await _service.RefreshAsync(token, "android", "1.2.3.4");

        Assert.Equal("access-token", result.Token);
        Assert.NotEqual(token, result.RefreshToken); // rotation is mandatory
        Assert.Equal(_user.Id, result.UserId);
    }

    [Fact]
    public async Task RefreshAsync_ValidToken_KeepsTheFamilyAndMarksRotation()
    {
        const string token = "refresh-token-2";
        var familyId = Guid.NewGuid();
        var session = ActiveSession(token, familyId);
        _sessionRepoMock
            .Setup(r => r.GetByRefreshTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        Session? replacement = null;
        _sessionRepoMock
            .Setup(r => r.TryRotateAsync(session.Id, It.IsAny<Session>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Session, DateTime, CancellationToken>((_, s, _, _) => replacement = s)
            .ReturnsAsync(true);

        var result = await _service.RefreshAsync(token, "android", null);

        // The successor is in the same family, with the new token hash
        Assert.NotNull(replacement);
        Assert.Equal(familyId, replacement!.FamilyId);
        Assert.Equal(_user.Id, replacement.UserId);
        Assert.Equal(SessionService.HashRefreshToken(result.RefreshToken), replacement.RefreshTokenHash);
    }

    [Fact]
    public async Task RefreshAsync_UnknownToken_ThrowsUnauthorized()
    {
        _sessionRepoMock
            .Setup(r => r.GetByRefreshTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Session?)null);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.RefreshAsync("nope", null, null));

        Assert.Equal("INVALID_REFRESH_TOKEN", ex.ErrorCode);
    }

    [Fact]
    public async Task RefreshAsync_ExpiredSession_ThrowsUnauthorized()
    {
        const string token = "old";
        var session = ActiveSession(token);
        session.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        _sessionRepoMock
            .Setup(r => r.GetByRefreshTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.RefreshAsync(token, null, null));

        Assert.Equal("REFRESH_TOKEN_EXPIRED", ex.ErrorCode);
    }

    [Fact]
    public async Task RefreshAsync_SessionRevokedByLogout_ThrowsUnauthorized()
    {
        // Revoked without ReplacedBySessionId = deliberate logout, not rotation
        const string token = "logged-out";
        var session = ActiveSession(token);
        session.RevokedAt = DateTime.UtcNow.AddMinutes(-1);
        _sessionRepoMock
            .Setup(r => r.GetByRefreshTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.RefreshAsync(token, null, null));

        Assert.Equal("SESSION_REVOKED", ex.ErrorCode);
        _sessionRepoMock.Verify(
            r => r.RevokeFamilyAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RefreshAsync_RotatedWithinGraceWindow_SucceedsWithoutRevokingFamily()
    {
        // The token was already rotated 5 seconds ago: this is a race of two parallel
        // client requests, not theft. The losing request must get a working pair.
        const string token = "raced";
        var session = ActiveSession(token);
        session.RevokedAt = DateTime.UtcNow.AddSeconds(-5);
        session.ReplacedBySessionId = Guid.NewGuid();
        _sessionRepoMock
            .Setup(r => r.GetByRefreshTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var result = await _service.RefreshAsync(token, "android", null);

        Assert.Equal("access-token", result.Token);
        Assert.False(string.IsNullOrWhiteSpace(result.RefreshToken));
        _sessionRepoMock.Verify(
            r => r.RevokeFamilyAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RefreshAsync_RotatedAfterGraceWindow_RevokesFamilyAndThrows()
    {
        // The same scenario, but a minute later: this is reuse of a stolen
        // token, so the whole chain is revoked.
        const string token = "stolen";
        var familyId = Guid.NewGuid();
        var session = ActiveSession(token, familyId);
        session.RevokedAt = DateTime.UtcNow.AddSeconds(-(GraceSeconds + 30));
        session.ReplacedBySessionId = Guid.NewGuid();
        _sessionRepoMock
            .Setup(r => r.GetByRefreshTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.RefreshAsync(token, null, null));

        Assert.Equal("REFRESH_TOKEN_REUSED", ex.ErrorCode);
        _sessionRepoMock.Verify(
            r => r.RevokeFamilyAsync(familyId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RefreshAsync_LostTheRotationRace_StillReturnsWorkingPair()
    {
        // The session looked active, but a parallel request managed to rotate
        // it between SELECT and UPDATE. The client must not be kicked out.
        const string token = "raced-2";
        var session = ActiveSession(token);
        _sessionRepoMock
            .Setup(r => r.GetByRefreshTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _sessionRepoMock
            .Setup(r => r.TryRotateAsync(session.Id, It.IsAny<Session>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _service.RefreshAsync(token, "android", null);

        Assert.False(string.IsNullOrWhiteSpace(result.RefreshToken));
        _sessionRepoMock.Verify(
            r => r.RevokeFamilyAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }


    [Fact]
    public async Task RefreshAsync_GraceWindowButFamilyLoggedOut_ThrowsRevoked()
    {
        // The token was rotated a second ago, but then the user logged out
        // (logout / logout-all) and the whole chain was revoked. The grace window must not
        // become a loophole around signing out of the account.
        const string token = "raced-but-logged-out";
        var session = ActiveSession(token);
        session.RevokedAt = DateTime.UtcNow.AddSeconds(-1);
        session.ReplacedBySessionId = Guid.NewGuid();

        _sessionRepoMock
            .Setup(r => r.GetByRefreshTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _sessionRepoMock
            .Setup(r => r.HasLiveSessionInFamilyAsync(session.FamilyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.RefreshAsync(token, null, null));

        Assert.Equal("SESSION_REVOKED", ex.ErrorCode);
        _sessionRepoMock.Verify(
            r => r.CreateAsync(It.IsAny<Session>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefreshAsync_InactiveUser_RevokesEverythingAndThrows()
    {
        const string token = "deactivated";
        _user.IsActive = false;
        var session = ActiveSession(token);
        _sessionRepoMock
            .Setup(r => r.GetByRefreshTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.RefreshAsync(token, null, null));

        Assert.Equal("USER_INACTIVE", ex.ErrorCode);
        _sessionRepoMock.Verify(
            r => r.RevokeAllForUserAsync(_user.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RefreshAsync_DeletedUser_ThrowsUnauthorized()
    {
        const string token = "ghost";
        var session = ActiveSession(token);
        _sessionRepoMock
            .Setup(r => r.GetByRefreshTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
        _userRepoMock
            .Setup(r => r.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.RefreshAsync(token, null, null));

        Assert.Equal("USER_NOT_FOUND", ex.ErrorCode);
    }

    [Fact]
    public async Task RevokeAsync_KnownToken_RevokesThatSession()
    {
        const string token = "bye";
        var session = ActiveSession(token);
        _sessionRepoMock
            .Setup(r => r.GetByRefreshTokenHashAsync(SessionService.HashRefreshToken(token), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        await _service.RevokeAsync(token);

        _sessionRepoMock.Verify(
            r => r.RevokeAsync(session.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeAsync_UnknownToken_DoesNotThrow()
    {
        // Logout must be idempotent and must not reveal whether the token exists
        _sessionRepoMock
            .Setup(r => r.GetByRefreshTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Session?)null);

        await _service.RevokeAsync("whatever");
        _sessionRepoMock.Verify(
            r => r.RevokeAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RevokeAsync_NullOrEmptyToken_DoesNothing()
    {
        await _service.RevokeAsync(null);
        await _service.RevokeAsync("");

        _sessionRepoMock.Verify(
            r => r.GetByRefreshTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RevokeAllForUserAsync_DelegatesToRepository()
    {
        await _service.RevokeAllForUserAsync(_user.Id);

        _sessionRepoMock.Verify(
            r => r.RevokeAllForUserAsync(_user.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
