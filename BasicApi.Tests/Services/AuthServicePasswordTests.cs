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
        _service = new AuthService(_users.Object, Mock.Of<IJwtService>(), _sessions.Object, new HubConnectionRegistry());
    }

    private static RegisterRequestDto Register(string password) =>
        new() { Username = "alice", Email = "alice@test.local", Password = password };

    [Theory]
    [InlineData(73, 'a')]   // ASCII: 73 байта
    [InlineData(37, 'ж')]   // кириллица: 37 символов = 74 байта
    public async Task Register_PasswordLongerThan72Bytes_IsRejected(int length, char ch)
    {
        // bcrypt молча обрезает пароль до 72 байт: два разных длинных пароля
        // с общим началом оказались бы одним и тем же паролем.
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
        // Без проверки хеша ответ «нет такого пользователя» приходил мгновенно,
        // а «неверный пароль» — через ~100 мс bcrypt: по времени можно было
        // перебирать существующие логины.
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
