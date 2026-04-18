using System.Data;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace BasicApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DatabaseTestController(IDbConnection connection) : ControllerBase
{
    [HttpGet("ping")]
    public async Task<IActionResult> Ping()
    {
        try
        {
            var result = await connection.ExecuteScalarAsync<int>("SELECT 1");
            return Ok(new { status = "OK", result });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { status = "ERROR", error = ex.Message });
        }
    }
}