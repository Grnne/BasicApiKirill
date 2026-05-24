using System.IdentityModel.Tokens.Jwt;
using BasicApi.Services;
using Microsoft.Extensions.Configuration;

namespace BasicApi.Tests.Services;

public class JwtServiceTests
{
    private static JwtService CreateJwtService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "ThisIsASuperSecretKeyForTestingPurposes123!",
                ["Jwt:Issuer"] = "test-issuer",
                ["Jwt:Audience"] = "test-audience",
                ["Jwt:ExpiryMinutes"] = "60"
            })
            .Build();

        return new JwtService(config);
    }

    [Fact]
    public void GenerateToken_ReturnsNonEmptyString()
    {
        // Arrange
        var jwt = CreateJwtService();

        // Act
        var token = jwt.GenerateToken(Guid.NewGuid(), "testuser", "test@example.com");

        // Assert
        Assert.False(string.IsNullOrEmpty(token));
    }

    [Fact]
    public void ValidateToken_ValidToken_ReturnsClaimsPrincipal()
    {
        // Arrange
        var jwt = CreateJwtService();
        var userId = Guid.NewGuid();
        var token = jwt.GenerateToken(userId, "testuser", "test@example.com");

        // Act
        var principal = jwt.ValidateToken(token);

        // Assert
        Assert.NotNull(principal);

        // JwtSecurityTokenHandler maps "sub" -> ClaimTypes.NameIdentifier by default
        Assert.Equal(userId.ToString(),
            principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal("testuser",
            principal.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value);
        Assert.Equal("test@example.com",
            principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value);
    }

    [Fact]
    public void TryValidateToken_ValidToken_ReturnsTrueWithUserIdAndUsername()
    {
        // Arrange
        var jwt = CreateJwtService();
        var expectedUserId = Guid.NewGuid();
        const string expectedUsername = "testuser";
        var token = jwt.GenerateToken(expectedUserId, expectedUsername, "test@example.com");

        // Act
        var result = jwt.TryValidateToken(token, out var actualUserId, out var actualUsername);

        // Assert
        Assert.True(result);
        Assert.Equal(expectedUserId, actualUserId);
        Assert.Equal(expectedUsername, actualUsername);
    }

    [Fact]
    public void TryValidateToken_InvalidToken_ReturnsFalseWithEmptyOutValues()
    {
        // Arrange
        var jwt = CreateJwtService();

        // Act
        var result = jwt.TryValidateToken("invalid-token-that-is-definitely-not-valid", out var userId, out var username);

        // Assert
        Assert.False(result);
        Assert.Equal(Guid.Empty, userId);
        Assert.Equal(string.Empty, username);
    }

    [Fact]
    public void GetExpiryDate_ReturnsFutureDate()
    {
        // Arrange
        var jwt = CreateJwtService();
        var before = DateTime.UtcNow;

        // Act
        var expiry = jwt.GetExpiryDate();
        var after = DateTime.UtcNow.AddMinutes(60);

        // Assert — should be roughly now + 60 minutes
        Assert.True(expiry > before.AddMinutes(55));
        Assert.True(expiry <= after);
    }
}
