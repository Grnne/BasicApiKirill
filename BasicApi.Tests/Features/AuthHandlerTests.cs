using BasicApi.Features.Auth;
using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Auth;
using BasicApi.Services;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BasicApi.Tests.Features;

public class AuthHandlerTests
{
    private readonly Mock<IUserRepository> _userRepoMock;
    private readonly Mock<IJwtService> _jwtServiceMock;
    private readonly AuthHandler _handler;

    public AuthHandlerTests()
    {
        _userRepoMock = new Mock<IUserRepository>();
        _jwtServiceMock = new Mock<IJwtService>();
        _handler = new AuthHandler(_userRepoMock.Object, _jwtServiceMock.Object);
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsOkWithAuthResponse()
    {
        // Arrange
        var password = "correct-password";
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "testuser",
            Email = "test@example.com",
            DisplayName = "Test User",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password)
        };

        var token = "jwt-token";
        var expiresAt = DateTime.UtcNow.AddHours(1);

        _userRepoMock
            .Setup(r => r.GetByUsernameOrEmailAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _jwtServiceMock.Setup(s => s.GenerateToken(user.Id, user.Username, user.Email)).Returns(token);
        _jwtServiceMock.Setup(s => s.GetExpiryDate()).Returns(expiresAt);

        // Act
        var result = await _handler.LoginAsync(new LoginRequestDto
        {
            UsernameOrEmail = "testuser",
            Password = password
        });

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AuthResponseDto>(okResult.Value);
        Assert.Equal(user.Id, response.UserId);
        Assert.Equal(user.Username, response.Username);
        Assert.Equal(user.Email, response.Email);
        Assert.Equal(user.DisplayName, response.DisplayName);
        Assert.Equal(token, response.Token);
        Assert.Equal(expiresAt, response.ExpiresAt);
    }

    [Fact]
    public async Task LoginAsync_InvalidPassword_ThrowsUnauthorizedAccess()
    {
        // Arrange
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = "testuser",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correct-password")
        };

        _userRepoMock
            .Setup(r => r.GetByUsernameOrEmailAsync("testuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _handler.LoginAsync(new LoginRequestDto
            {
                UsernameOrEmail = "testuser",
                Password = "wrong-password"
            }));

        Assert.Contains("Invalid", ex.Message);
    }

    [Fact]
    public async Task LoginAsync_UserNotFound_ThrowsUnauthorized()
    {
        // Arrange
        _userRepoMock
            .Setup(r => r.GetByUsernameOrEmailAsync("unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act & Assert
        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _handler.LoginAsync(new LoginRequestDto
            {
                UsernameOrEmail = "unknown",
                Password = "any"
            }));
    }

    [Fact]
    public async Task RegisterAsync_Success_ReturnsCreatedWithAuthResponse()
    {
        // Arrange
        var token = "jwt-token";
        var expiresAt = DateTime.UtcNow.AddHours(1);
        var userId = Guid.NewGuid();

        _userRepoMock
            .Setup(r => r.GetByUsernameOrEmailAsync("newuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        _userRepoMock
            .Setup(r => r.GetByUsernameOrEmailAsync("new@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        _userRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(userId);

        _jwtServiceMock.Setup(s => s.GenerateToken(userId, "newuser", "new@example.com")).Returns(token);
        _jwtServiceMock.Setup(s => s.GetExpiryDate()).Returns(expiresAt);

        // Act
        var result = await _handler.RegisterAsync(new RegisterRequestDto
        {
            Username = "newuser",
            Email = "new@example.com",
            Password = "StrongPass1!",
            DisplayName = "New User"
        });

        // Assert
        var createdResult = Assert.IsType<CreatedResult>(result);
        var response = Assert.IsType<AuthResponseDto>(createdResult.Value);
        Assert.Equal(userId, response.UserId);
        Assert.Equal("newuser", response.Username);
        Assert.Equal("new@example.com", response.Email);
        Assert.Equal("New User", response.DisplayName);
        Assert.Equal(token, response.Token);
        Assert.Equal(expiresAt, response.ExpiresAt);
    }

    [Fact]
    public async Task RegisterAsync_DuplicateUsername_ThrowsConflictException()
    {
        // Arrange
        _userRepoMock
            .Setup(r => r.GetByUsernameOrEmailAsync("existing", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Username = "existing" });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _handler.RegisterAsync(new RegisterRequestDto
            {
                Username = "existing",
                Email = "new@example.com",
                Password = "StrongPass1!"
            }));

        Assert.Contains("Username already exists", ex.Message);
    }

    [Fact]
    public async Task RegisterAsync_DuplicateEmail_ThrowsConflictException()
    {
        // Arrange
        _userRepoMock
            .Setup(r => r.GetByUsernameOrEmailAsync("newuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        _userRepoMock
            .Setup(r => r.GetByUsernameOrEmailAsync("existing@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User { Email = "existing@example.com" });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _handler.RegisterAsync(new RegisterRequestDto
            {
                Username = "newuser",
                Email = "existing@example.com",
                Password = "StrongPass1!"
            }));

        Assert.Contains("Email already exists", ex.Message);
    }

    [Fact]
    public async Task RegisterAsync_EmptyDisplayName_UsesUsername()
    {
        // Arrange
        _userRepoMock
            .Setup(r => r.GetByUsernameOrEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        _userRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        _jwtServiceMock.Setup(s => s.GenerateToken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns("token");
        _jwtServiceMock.Setup(s => s.GetExpiryDate()).Returns(DateTime.UtcNow.AddHours(1));

        // Act
        var result = await _handler.RegisterAsync(new RegisterRequestDto
        {
            Username = "user_no_display",
            Email = "user@example.com",
            Password = "StrongPass1!"
        });

        // Assert
        var createdResult = Assert.IsType<CreatedResult>(result);
        var response = Assert.IsType<AuthResponseDto>(createdResult.Value);
        Assert.Equal("user_no_display", response.DisplayName);
    }
}
