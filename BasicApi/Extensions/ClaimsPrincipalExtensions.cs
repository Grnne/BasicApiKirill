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

    /// <summary>Session chain the access token was issued for (claim <c>sid</c>); null if the claim is missing.</summary>
    public static Guid? GetSessionFamilyId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(JwtRegisteredClaimNames.Sid) ??
                    user.FindFirstValue(ClaimTypes.Sid);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    /// <summary>When the access token expires (claim <c>exp</c>); null if the claim is missing.</summary>
    public static DateTimeOffset? GetTokenExpiry(this ClaimsPrincipal user) =>
        long.TryParse(user.FindFirstValue(JwtRegisteredClaimNames.Exp), out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
}