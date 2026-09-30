using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BasicApi.IntegrationTests.Features.Media;

namespace BasicApi.IntegrationTests.E2E;

/// <summary>
/// Files through the real stack: links signed for the site's own address, the storage behind
/// Caddy at /media/, and the headers that keep uploaded content inert (plan 2, F4).
/// </summary>
[Trait("Category", "E2E")]
[Collection(E2ECollection.Name)]
public class MediaE2ETests(E2EUsers users)
{
    /// <summary>A browser's request to the storage: no API token, the site's certificate.</summary>
    private static HttpClient Storage() => new(E2EEnvironment.CreateHandler());

    private static async Task<JsonElement> UploadAsync(HttpClient api, string kind, byte[] content, string name, string mime)
    {
        var start = await api.PostAsJsonAsync("api/media/uploads", new { kind, fileName = name, mimeType = mime, size = content.Length });
        Assert.True(start.StatusCode == HttpStatusCode.Created, await start.Content.ReadAsStringAsync());
        var ticket = await start.ReadAsync<JsonElement>();
        var url = ticket.GetProperty("uploadUrl").GetString()!;
        Assert.StartsWith(E2EEnvironment.BaseUrl + "/media/", url);

        using var body = new ByteArrayContent(content);
        body.Headers.ContentType = MediaTypeHeaderValue.Parse(ticket.GetProperty("contentType").GetString()!);
        using var storage = Storage();
        var put = await storage.PutAsync(url, body);
        Assert.True(put.IsSuccessStatusCode, $"PUT {(int)put.StatusCode} {await put.Content.ReadAsStringAsync()}");

        var complete = await api.PostAsync($"api/media/uploads/{ticket.GetProperty("attachmentId").GetGuid()}/complete", null);
        Assert.True(complete.IsSuccessStatusCode, await complete.Content.ReadAsStringAsync());
        return await complete.ReadAsync<JsonElement>();
    }

    [E2EFact]
    public async Task Files_GoThroughTheSite_AndStayInert()
    {
        using var aliceApi = E2EEnvironment.CreateClient(users.Alice.Token);
        using var bobApi = E2EEnvironment.CreateClient(users.Bob.Token);
        var chat = (await (await aliceApi.PostAsync($"api/chats/private/{users.Bob.UserId}", null))
            .ReadAsync<JsonElement>()).GetProperty("chatId").GetGuid();

        var png = MediaUploadTests.Png(800, 600);
        var photo = await UploadAsync(aliceApi, "photo", png, "photo.png", "image/png");
        Assert.Equal(800, photo.GetProperty("width").GetInt32());
        var page = Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>");
        var file = await UploadAsync(aliceApi, "file", page, "page.html", "text/html");

        var sent = await aliceApi.PostAsJsonAsync($"api/chats/{chat}/messages", new
        {
            text = "e2e album",
            attachmentIds = new[] { photo.GetProperty("id").GetGuid() }
        });
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        await aliceApi.PostAsJsonAsync($"api/chats/{chat}/messages", new { attachmentIds = new[] { file.GetProperty("id").GetGuid() } });

        var links = await (await bobApi.PostAsJsonAsync("api/media/links", new
        {
            attachmentIds = new[] { photo.GetProperty("id").GetGuid(), file.GetProperty("id").GetGuid() }
        })).ReadAsync<JsonElement>();
        var items = links.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("attachmentId").GetGuid());
        using var storage = Storage();

        var original = await storage.GetAsync(items[photo.GetProperty("id").GetGuid()].GetProperty("url").GetString());
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        Assert.Equal(png, await original.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", original.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("sandbox", original.Headers.GetValues("Content-Security-Policy").Single());
        Assert.False(original.Headers.Contains("Server"));

        var thumbnail = await storage.GetAsync(items[photo.GetProperty("id").GetGuid()].GetProperty("thumbnailUrl").GetString());
        Assert.Equal("image/jpeg", thumbnail.Content.Headers.ContentType!.MediaType);

        var html = await storage.GetAsync(items[file.GetProperty("id").GetGuid()].GetProperty("url").GetString());
        Assert.Equal("application/octet-stream", html.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", html.Content.Headers.ContentDisposition!.DispositionType);

        // Without a signature the storage gives nothing away.
        var bare = new Uri(items[photo.GetProperty("id").GetGuid()].GetProperty("url").GetString()!).GetLeftPart(UriPartial.Path);
        Assert.Equal(HttpStatusCode.Forbidden, (await storage.GetAsync(bare)).StatusCode);
    }
}
