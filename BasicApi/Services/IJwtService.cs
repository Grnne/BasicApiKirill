using System.Security.Claims;

namespace BasicApi.Services;

public interface IJwtService
{
    string GenerateToken(Guid userId, string username, string email);

    /// <summary>
    /// Access token bound to a session chain (claim <c>sid</c> = session family id),
    /// so a long-lived hub connection can be checked against the session and cut
    /// when it is revoked.
    /// </summary>
    string GenerateToken(Guid userId, string username, string email, Guid sessionFamilyId);
    ClaimsPrincipal? ValidateToken(string token);
    bool TryValidateToken(string token, out Guid userId, out string username);
    DateTime GetExpiryDate();
}