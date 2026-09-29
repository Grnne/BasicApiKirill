using System.Net;
using System.Text.RegularExpressions;
using BasicApi.IntegrationTests.Infrastructure;

namespace BasicApi.IntegrationTests.Api;

[Collection(PostgresCollection.Name)]
public class SecurityHeadersTests(PostgresFixture db)
{
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/signalr-docs")]
    [InlineData("/api/chats")] // 401 — headers are needed on error responses too
    public async Task Responses_CarryContentSecurityPolicy(string path)
    {
        await using var factory = new ApiFactory(db.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("script-src 'self'", csp);
        Assert.Contains("connect-src 'self'", csp);   // the token will not go to a foreign host even under XSS
        Assert.Contains("frame-ancestors 'none'", csp); // does not work in <meta> — only as a header
        Assert.Contains("object-src 'none'", csp);
        Assert.Contains("base-uri 'none'", csp);
        Assert.DoesNotContain("unsafe-eval", csp);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }

    [Fact]
    public async Task SwaggerUi_HasNoInlineScripts_SoStrictPolicyDoesNotBreakIt()
    {
        await using var factory = new ApiFactory(db.ConnectionString,
            new Dictionary<string, string?> { ["Swagger:Enabled"] = "true" });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/swagger/index.html");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var inlineScripts = Regex.Matches(html, @"<script(?![^>]*\bsrc=)[^>]*>");
        Assert.Empty(inlineScripts);
    }
}
