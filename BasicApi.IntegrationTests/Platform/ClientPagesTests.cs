using System.Net;
using BasicApi.IntegrationTests.Infrastructure;

namespace BasicApi.IntegrationTests.Platform;

/// <summary>The web client's pages as the API serves them.</summary>
[Collection(PostgresCollection.Name)]
public class ClientPagesTests(PostgresFixture db) : IDisposable
{
    private readonly string _webRoot = Directory.CreateTempSubdirectory("client-pages-").FullName;

    public void Dispose() => Directory.Delete(_webRoot, recursive: true);

    [Theory]
    [InlineData("/client/")]
    [InlineData("/client/index.html")]
    // A route of the client, the address a reload and a notification click open.
    [InlineData("/client/chat")]
    public async Task ThePage_IsRevalidated_SoADeployIsPickedUp(string path)
    {
        // The bug: /client/chat came back without Cache-Control; the browser kept the page by its
        // own guess and after a deploy asked for scripts that were no longer there — a blank page.
        Directory.CreateDirectory(Path.Combine(_webRoot, "client"));
        await File.WriteAllTextAsync(Path.Combine(_webRoot, "client", "index.html"), "<!doctype html><title>t</title>");
        await using var factory = new ApiFactory(db.ConnectionString, new Dictionary<string, string?> { ["webroot"] = _webRoot });
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
    }
}
