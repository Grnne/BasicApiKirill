using System.Net;
using System.Text.RegularExpressions;
using BasicApi.IntegrationTests.Infrastructure;

namespace BasicApi.IntegrationTests.Platform;

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
    public async Task LocalFilePreviews_AreAllowedAsPicturesAndMedia_NowhereElse()
    {
        // The client previews chosen files and measures videos before upload through blob: URLs,
        // which only the page itself can create; scripts and requests stay 'self'.
        await using var factory = new ApiFactory(db.ConnectionString);
        using var client = factory.CreateClient();

        var csp = Assert.Single((await client.GetAsync("/health/live")).Headers.GetValues("Content-Security-Policy"));
        var directives = csp.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("img-src 'self' data: blob:", directives);
        Assert.Contains("media-src 'self' blob:", directives);
        Assert.All(directives.Where(d => !d.StartsWith("img-src") && !d.StartsWith("media-src")),
            d => Assert.DoesNotContain("blob:", d));
    }

    [Fact]
    public async Task FilesFromAnotherOrigin_AreAllowedOnlyFromTheStorage()
    {
        await using var factory = new ApiFactory(db.ConnectionString, new Dictionary<string, string?>
        {
            ["Storage:Endpoint"] = "http://seaweedfs:8333",
            ["Storage:PublicUrl"] = "http://localhost:8333",
            ["Storage:AccessKey"] = "key",
            ["Storage:SecretKey"] = "secret"
        });
        using var client = factory.CreateClient();

        var csp = Assert.Single((await client.GetAsync("/health/live")).Headers.GetValues("Content-Security-Policy"));

        Assert.Contains("img-src 'self' data: blob: http://localhost:8333;", csp);
        Assert.Contains("media-src 'self' blob: http://localhost:8333;", csp);
        Assert.Contains("connect-src 'self' http://localhost:8333;", csp);
        Assert.DoesNotContain("seaweedfs", csp); // the internal address is nobody's business
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
