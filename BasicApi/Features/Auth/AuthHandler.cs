using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Auth;
using BasicApi.Services;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BasicApi.Features.Auth;

public class AuthHandler(
    IUserRepository userRepository,
    IJwtService jwtService)
{
    public async Task<IActionResult> LoginAsync(
    LoginRequestDto request)
    {
        var user = await userRepository.GetByUsernameOrEmailAsync(request.UsernameOrEmail);

                if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedException("Invalid username/email or password", "INVALID_CREDENTIALS");

        var token = jwtService.GenerateToken(user.Id, user.Username, user.Email);

        return new OkObjectResult(new AuthResponseDto
        {
            UserId = user.Id,
            Username = user.Username,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Token = token,
            ExpiresAt = jwtService.GetExpiryDate()
        });
    }

    public async Task<IActionResult> RegisterAsync(
        RegisterRequestDto request)
    {
        var existingUser = await userRepository.GetByUsernameOrEmailAsync(request.Username);

                if (existingUser != null)
            throw new ConflictException("Username already exists", "USERNAME_TAKEN");

        // Проверка уникальности email
        existingUser = await userRepository.GetByUsernameOrEmailAsync(request.Email);

                if (existingUser != null)
            throw new ConflictException("Email already exists", "EMAIL_TAKEN");

        var user = new User
        {
            Username = request.Username,
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            DisplayName = string.IsNullOrEmpty(request.DisplayName)
                ? request.Username
                : request.DisplayName,
            CreatedAt = DateTime.UtcNow
        };

        var userId = await userRepository.CreateAsync(user);
        var token = jwtService.GenerateToken(userId, user.Username, user.Email);

        return new CreatedResult(string.Empty, new AuthResponseDto
        {
            UserId = userId,
            Username = user.Username,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Token = token,
            ExpiresAt = jwtService.GetExpiryDate()
        });
    }
}