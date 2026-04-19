using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BasicApi.Features.Users;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Tags("Users")]
public class UsersController(UsersHandler handlers) : ControllerBase
{
    /// <summary>
    /// Получить ID пользователя по имени пользователя или email
    /// </summary>
    /// <remarks>
    /// Пример запроса:
    /// 
    ///     GET /api/users/GetUserId/john_doe
    ///     GET /api/users/GetUserId/john@example.com
    ///     
    /// </remarks>
    /// <param name="usernameOrEmail">Имя пользователя или email</param>
    /// <returns>ID пользователя</returns>
    /// <response code="200">Возвращает ID пользователя</response>
    /// <response code="400">Пользователь не найден</response>
    /// <response code="401">Пользователь не авторизован</response>
    [Authorize]
    [HttpGet("GetUserId/{usernameOrEmail}")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetUserId(string usernameOrEmail)
        => await handlers.GetUserIdAsync(usernameOrEmail);
}