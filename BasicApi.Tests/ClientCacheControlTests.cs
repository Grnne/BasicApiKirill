namespace BasicApi.Tests;

public class ClientCacheControlTests
{
    [Theory]
    [InlineData("/client/assets/index-DfIV3HkR.js", "public,max-age=31536000,immutable")]
    [InlineData("/client/index.html", "no-cache")]
    // The service worker has a fixed name: cached, it would keep an old one handling notifications.
    [InlineData("/client/sw.js", "no-cache")]
    [InlineData("/client/favicon.svg", null)]
    [InlineData("/signalr-docs.html", null)]
    public void ClientFiles_AreCachedByWhetherTheirNameChangesWithTheContent(string path, string? expected)
    {
        Assert.Equal(expected, Program.ClientCacheControl(path));
    }
}
