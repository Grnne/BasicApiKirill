using BasicApi.Models;
using BasicApi.Models.Dto.Auth;
using BasicApi.Services;
using BasicApi.Storage.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BasicApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Tags("Users")]
public class UsersController(IUserRepository userRepository) : ControllerBase
{
    [Authorize]
    [HttpGet("GetUserId/{usernameOrEmail}")]
    public async Task<IActionResult> Login(string usernameOrEmail)
    {
        var userId = await userRepository.GetIdByUsernameOrEmailAsync(usernameOrEmail);
        
        if(!userId.HasValue || userId == Guid.Empty)
        {
            return BadRequest("плохой запрос");
        }

        return Ok(userId);
    }
}