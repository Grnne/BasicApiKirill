using System.Security.Claims;

namespace BasicApi.Services;

public interface IJwtService
{
    string GenerateToken(Guid userId, string username, string email);
    ClaimsPrincipal? ValidateToken(string token);
    bool TryValidateToken(string token, out Guid userId, out string username);
    DateTime GetExpiryDate();
}