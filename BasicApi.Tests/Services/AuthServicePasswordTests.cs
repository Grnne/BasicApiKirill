using System.Diagnostics;
using BasicApi.Hubs;
using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Auth;
using BasicApi.Services;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Moq;

namespace BasicApi.Tests.Services;

public class AuthServicePasswordTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ISessionService> _sessions = new();
    private readonly AuthService _service;

    public AuthServicePasswordTests()
    {
        _users.Setup(r => r.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        _sessions.Setup(s => s.IssueForUserAsync(It.IsAny<User>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthResponseDto());
        _service = new AuthService(_users.Object, Mock.Of<IJwtService>(), _sessions.Object, Mock.Of<IDeviceRepository>(), new HubConnectionRegistry());
    }

    private static RegisterRequestDto Register(string password) =>
        new() { Username = "alice", Email = "alice@test.local", Password = password };

    [Theory]
    [InlineData(73, 'a')]   // ASCII: 73 bytes
    [InlineData(37, 'ж')]   // Cyrillic: 37 characters = 74 bytes
    public async Task Register_PasswordLongerThan72Bytes_IsRejected(int length, char ch)
    {
        // bcrypt silently truncates a password to 72 bytes: two different long passwords
        // with a common beginning would turn out to be the same password.
        var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
            _service.RegisterAsync(Register(new string(ch, length))));

        Assert.Equal("PASSWORD_TOO_LONG", ex.ErrorCode);
        _users.Verify(r => r.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Register_PasswordOfExactly72Bytes_IsAccepted()
    {
        await _service.RegisterAsync(Register(new string('a', 72)));

        _users.Verify(r => r.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Login_UnknownUser_StillSpendsTimeOnPasswordHashing()
    {
        // Without a hash check, the "no such user" response came instantly, while "wrong password"
        // took ~100 ms of bcrypt: timing made it possible to enumerate existing logins.
        _users.Setup(r => r.GetByUsernameOrEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.LoginAsync(new LoginRequestDto { UsernameOrEmail = "warmup", Password = "secret123" }));

        var watch = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.LoginAsync(new LoginRequestDto { UsernameOrEmail = "nobody", Password = "secret123" }));
        watch.Stop();

        Assert.Equal("INVALID_CREDENTIALS", ex.ErrorCode);
        Assert.True(watch.ElapsedMilliseconds >= 20,
            $"unknown-user login answered in {watch.ElapsedMilliseconds} ms — no bcrypt work was done");
    }
}
