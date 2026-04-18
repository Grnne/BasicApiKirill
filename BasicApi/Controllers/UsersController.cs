using BasicApi.Models;
using Microsoft.AspNetCore.Mvc;

namespace BasicApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Tags("Users")]
public class UsersController : ControllerBase
{
    private static List<User> _users = new()
    {
        new User { Id = 1, Name = "Иван Петров", Email = "ivan@example.com", Age = 25 },
        new User { Id = 2, Name = "Мария Сидорова", Email = "maria@example.com", Age = 30 },
        new User { Id = 3, Name = "Алексей Смирнов", Email = "alex@example.com", Age = 28 }
    };

    /// <summary>
    /// Получить список всех пользователей
    /// </summary>
    /// <returns>Список пользователей</returns>
    /// <response code="200">Успешное выполнение</response>
    [HttpGet]
    [ProducesResponseType(typeof(List<User>), StatusCodes.Status200OK)]
    public IActionResult GetAll()
    {
        return Ok(_users);
    }

    /// <summary>
    /// Получить пользователя по ID
    /// </summary>
    /// <param name="id">Идентификатор пользователя</param>
    /// <returns>Пользователь</returns>
    /// <response code="200">Пользователь найден</response>
    /// <response code="404">Пользователь не найден</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(User), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetById(int id)
    {
        var user = _users.FirstOrDefault(u => u.Id == id);
        if (user == null)
            return NotFound(new { message = $"Пользователь с ID {id} не найден" });

        return Ok(user);
    }

    /// <summary>
    /// Создать нового пользователя
    /// </summary>
    /// <param name="request">Данные нового пользователя</param>
    /// <returns>Созданный пользователь</returns>
    /// <response code="201">Пользователь успешно создан</response>
    /// <response code="400">Неверные данные запроса</response>
    [HttpPost]
    [ProducesResponseType(typeof(User), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Create([FromBody] CreateUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Имя пользователя обязательно" });

        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { message = "Email обязателен" });

        if (request.Age < 0 || request.Age > 120)
            return BadRequest(new { message = "Возраст должен быть от 0 до 120 лет" });

        var newId = _users.Max(u => u.Id) + 1;
        var user = new User
        {
            Id = newId,
            Name = request.Name,
            Email = request.Email,
            Age = request.Age
        };

        _users.Add(user);
        return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
    }

    /// <summary>
    /// Обновить данные пользователя
    /// </summary>
    /// <param name="id">Идентификатор пользователя</param>
    /// <param name="request">Обновленные данные</param>
    /// <returns>Обновленный пользователь</returns>
    /// <response code="200">Пользователь успешно обновлен</response>
    /// <response code="404">Пользователь не найден</response>
    /// <response code="400">Неверные данные запроса</response>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(User), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Update(int id, [FromBody] UpdateUserRequest request)
    {
        var user = _users.FirstOrDefault(u => u.Id == id);
        if (user == null)
            return NotFound(new { message = $"Пользователь с ID {id} не найден" });

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Имя пользователя обязательно" });

        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { message = "Email обязателен" });

        if (request.Age < 0 || request.Age > 120)
            return BadRequest(new { message = "Возраст должен быть от 0 до 120 лет" });

        user.Name = request.Name;
        user.Email = request.Email;
        user.Age = request.Age;

        return Ok(user);
    }

    /// <summary>
    /// Удалить пользователя
    /// </summary>
    /// <param name="id">Идентификатор пользователя</param>
    /// <response code="204">Пользователь успешно удален</response>
    /// <response code="404">Пользователь не найден</response>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Delete(int id)
    {
        var user = _users.FirstOrDefault(u => u.Id == id);
        if (user == null)
            return NotFound(new { message = $"Пользователь с ID {id} не найден" });

        _users.Remove(user);
        return NoContent();
    }
}