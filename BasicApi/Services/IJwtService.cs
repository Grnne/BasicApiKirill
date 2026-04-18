using System.Security.Claims;

namespace BasicApi.Services;

public interface IJwtService
{
    string GenerateToken(Guid userId, string username, string email);
    ClaimsPrincipal? ValidateToken(string token);
    DateTime GetExpiryDate();
}