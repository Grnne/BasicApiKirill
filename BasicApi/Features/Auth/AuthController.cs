using BasicApi.Extensions;
using BasicApi.Models.Dto.Auth;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BasicApi.Features.Auth;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Tags("Authentication")]
public class AuthController(AuthHandler handler) : ControllerBase
{
    /// <summary>
    /// Аутентификация пользователя
    /// </summary>
    /// <remarks>
    /// Пример запроса:
    /// 
    ///     POST /api/auth/login
    ///     {
    ///         "usernameOrEmail": "john_doe",
    ///         "password": "securePassword123"
    ///     }
    ///     
    /// </remarks>
    /// <param name="request">Учетные данные пользователя</param>
    /// <returns>JWT токен и информация о пользователе</returns>
    /// <response code="200">Успешная аутентификация, возвращает JWT токен</response>
    /// <response code="400">Неверный формат запроса</response>
    /// <response code="401">Неверное имя пользователя/email или пароль</response>
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponseDto), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
        => await handler.LoginAsync(request);

    /// <summary>
    /// Регистрация нового пользователя
    /// </summary>
    /// <remarks>
    /// Пример запроса:
    /// 
    ///     POST /api/auth/register
    ///     {
    ///         "username": "john_doe",
    ///         "email": "john@example.com",
    ///         "password": "securePassword123",
    ///         "displayName": "John Doe"
    ///     }
    ///     
    /// </remarks>
    /// <param name="request">Данные для регистрации</param>
    /// <returns>JWT токен и информация о пользователе</returns>
    /// <response code="200">Пользователь успешно зарегистрирован</response>
    /// <response code="400">Неверные данные или пользователь уже существует</response>
    [AllowAnonymous]
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponseDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto request)
        => await handler.RegisterAsync(request);
}