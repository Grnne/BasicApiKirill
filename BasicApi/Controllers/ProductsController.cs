using BasicApi.Models;
using Microsoft.AspNetCore.Mvc;

namespace BasicApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Tags("Products")]
public class ProductsController : ControllerBase
{
    private static List<Product> _products =
    [
        new Product { Id = 1, Name = "Ноутбук", Price = 50000, InStock = true },
        new Product { Id = 2, Name = "Смартфон", Price = 30000, InStock = true },
        new Product { Id = 3, Name = "Наушники", Price = 5000, InStock = false }
    ];

    /// <summary>
    /// Получить список всех продуктов
    /// </summary>
    /// <returns>Список продуктов</returns>
    /// <response code="200">Возвращает список продуктов</response>
    [HttpGet]
    [ProducesResponseType(typeof(List<Product>), StatusCodes.Status200OK)]
    public IActionResult GetAll()
    {
        return Ok(_products);
    }

    /// <summary>
    /// Получить продукт по идентификатору
    /// </summary>
    /// <param name="id">Идентификатор продукта</param>
    /// <returns>Продукт</returns>
    /// <response code="200">Продукт найден</response>
    /// <response code="404">Продукт не найден</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(Product), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetById(int id)
    {
        var product = _products.FirstOrDefault(p => p.Id == id);
        if (product == null)
            return NotFound(new { message = $"Продукт с ID {id} не найден" });

        return Ok(product);
    }

    /// <summary>
    /// Создать новый продукт
    /// </summary>
    /// <param name="request">Данные нового продукта</param>
    /// <returns>Созданный продукт</returns>
    /// <response code="201">Продукт успешно создан</response>
    /// <response code="400">Неверные данные запроса</response>
    [HttpPost]
    [ProducesResponseType(typeof(Product), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Create([FromBody] CreateProductRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Название продукта обязательно" });

        if (request.Price <= 0)
            return BadRequest(new { message = "Цена должна быть больше 0" });

        var newId = _products.Max(p => p.Id) + 1;
        var product = new Product
        {
            Id = newId,
            Name = request.Name,
            Price = request.Price,
            InStock = request.InStock
        };

        _products.Add(product);
        return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
    }

    /// <summary>
    /// Обновить существующий продукт
    /// </summary>
    /// <param name="id">Идентификатор продукта</param>
    /// <param name="request">Обновленные данные продукта</param>
    /// <returns>Обновленный продукт</returns>
    /// <response code="200">Продукт успешно обновлен</response>
    /// <response code="404">Продукт не найден</response>
    /// <response code="400">Неверные данные запроса</response>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(Product), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Update(int id, [FromBody] UpdateProductRequest request)
    {
        var product = _products.FirstOrDefault(p => p.Id == id);
        if (product == null)
            return NotFound(new { message = $"Продукт с ID {id} не найден" });

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "Название продукта обязательно" });

        if (request.Price <= 0)
            return BadRequest(new { message = "Цена должна быть больше 0" });

        product.Name = request.Name;
        product.Price = request.Price;
        product.InStock = request.InStock;

        return Ok(product);
    }

    /// <summary>
    /// Удалить продукт
    /// </summary>
    /// <param name="id">Идентификатор продукта</param>
    /// <response code="204">Продукт успешно удален</response>
    /// <response code="404">Продукт не найден</response>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Delete(int id)
    {
        var product = _products.FirstOrDefault(p => p.Id == id);
        if (product == null)
            return NotFound(new { message = $"Продукт с ID {id} не найден" });

        _products.Remove(product);
        return NoContent();
    }
}