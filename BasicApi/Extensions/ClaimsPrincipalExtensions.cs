using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BasicApi.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var userIdClaim = user.FindFirstValue(ClaimTypes.NameIdentifier) ??
                          user.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.Parse(userIdClaim!);
    }

}