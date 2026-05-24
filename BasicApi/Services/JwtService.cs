using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace BasicApi.Services;

public class JwtService : IJwtService
{
    private readonly SymmetricSecurityKey _key;
    private readonly JwtSecurityTokenHandler _tokenHandler;
    private readonly int _expiryMinutes;
    private readonly string _issuer;
    private readonly string _audience;

    public JwtService(IConfiguration configuration)
    {
        var key = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is not configured");
        _issuer = configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("Jwt:Issuer is not configured");
        _audience = configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException("Jwt:Audience is not configured");
        _expiryMinutes = int.Parse(configuration["Jwt:ExpiryMinutes"] ?? "60");

        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        _tokenHandler = new JwtSecurityTokenHandler();
    }

    public string GenerateToken(Guid userId, string username, string email)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, username),
            new(JwtRegisteredClaimNames.Email, email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = GetExpiryDate(),
            Issuer = _issuer,
            Audience = _audience,
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256)
        };

        return _tokenHandler.WriteToken(_tokenHandler.CreateToken(tokenDescriptor));
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            return _tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = _key,
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = true,
                ValidAudience = _audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out _);
        }
        catch
        {
            return null;
        }
    }

    public bool TryValidateToken(string token, out Guid userId, out string username)
    {
        userId = Guid.Empty;
        username = string.Empty;

        var principal = ValidateToken(token);
        if (principal is null)
            return false;

        var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                          ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (userIdClaim is null || !Guid.TryParse(userIdClaim, out userId))
            return false;

        username = principal.FindFirst(ClaimTypes.Name)?.Value
                   ?? principal.FindFirst(JwtRegisteredClaimNames.UniqueName)?.Value
                   ?? string.Empty;
        return true;
    }

    public DateTime GetExpiryDate() => DateTime.UtcNow.AddMinutes(_expiryMinutes);
}