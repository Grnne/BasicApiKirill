using BasicApi.Models.Dto.Auth;
using BasicApi.Services;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BasicApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(IUserRepository userRepository, IJwtService jwtService) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var user = await userRepository.GetByUsernameOrEmailAsync(request.UsernameOrEmail);
        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Unauthorized(new ErrorResponseDto { Message = "Invalid username/email or password" });

        var token = jwtService.GenerateToken(user.Id, user.Username, user.Email);

        return Ok(new AuthResponseDto
        {
            UserId = user.Id,
            Username = user.Username,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Token = token,
            ExpiresAt = jwtService.GetExpiryDate()
        });
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var existingUser = await userRepository.GetByUsernameOrEmailAsync(request.Username);
        if (existingUser != null)
            return BadRequest(new ErrorResponseDto { Message = "Username already exists" });

        existingUser = await userRepository.GetByUsernameOrEmailAsync(request.Email);
        if (existingUser != null)
            return BadRequest(new ErrorResponseDto { Message = "Email already exists" });

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

        return Ok(new AuthResponseDto
        {
            UserId = userId,
            Username = user.Username,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Token = token,
            ExpiresAt = jwtService.GetExpiryDate()
        });
    }

    [HttpPost("logout")]
    [Authorize] // ← а logout уже требует токен
    public IActionResult Logout()
    {
        // Для MVP — токен инвалидируется на клиенте
        // Здесь можно добавить blacklist токенов позже
        return Ok(new { Message = "Logged out successfully" });
    }
}