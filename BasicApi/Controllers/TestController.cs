using Microsoft.AspNetCore.Mvc;

namespace BasicApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Tags("Test")]
public class TestController : ControllerBase
{
    /// <summary>
    /// Простой тестовый эндпоинт
    /// </summary>
    /// <returns>Приветственное сообщение</returns>
    /// <response code="200">API работает корректно</response>
    [HttpGet("hello")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult Hello()
    {
        return Ok(new
        {
            message = "Hello World!",
            timestamp = DateTime.Now,
            status = "API работает"
        });
    }

    /// <summary>
    /// Получить список статусов
    /// </summary>
    /// <returns>Список статусов</returns>
    /// <response code="200">Список статусов успешно получен</response>
    [HttpGet("status")]
    [ProducesResponseType(typeof(IEnumerable<object>), StatusCodes.Status200OK)]
    public IActionResult GetStatuses()
    {
        return Ok(new[]
        {
            new { Id = 1, Status = "Active", Description = "Активен" },
            new { Id = 2, Status = "Pending", Description = "Ожидает" },
            new { Id = 3, Status = "Completed", Description = "Завершен" }
        });
    }

    /// <summary>
    /// Получить случайное число
    /// </summary>
    /// <returns>Случайные данные</returns>
    /// <response code="200">Случайные данные успешно сгенерированы</response>
    [HttpGet("random")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult GetRandom()
    {
        var random = new Random();
        return Ok(new
        {
            number = random.Next(1, 100),
            date = DateTime.Now,
            guid = Guid.NewGuid()
        });
    }
}