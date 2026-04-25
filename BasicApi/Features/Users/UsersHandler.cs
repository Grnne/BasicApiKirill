using BasicApi.Middleware;
using BasicApi.Storage.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace BasicApi.Features.Users;

public class UsersHandler(IUserRepository userRepository)
{
    public async Task<IActionResult> GetUserIdAsync(string usernameOrEmail)
    {
        var userId = await userRepository.GetIdByUsernameOrEmailAsync(usernameOrEmail);

        if (!userId.HasValue || userId == Guid.Empty)
            throw new NotFoundException("User not found");

        return new OkObjectResult(new { userId = userId.Value });
    }
}