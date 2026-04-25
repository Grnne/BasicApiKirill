using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BasicApi.Features.Users;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[Tags("Users")]
public class UsersController(UsersHandler handlers) : ControllerBase
{
    /// <summary>
    /// Get a user's ID by username or email.
    /// </summary>
    [Authorize]
    [HttpGet("GetUserId/{usernameOrEmail}")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserId(string usernameOrEmail)
        => await handlers.GetUserIdAsync(usernameOrEmail);
}