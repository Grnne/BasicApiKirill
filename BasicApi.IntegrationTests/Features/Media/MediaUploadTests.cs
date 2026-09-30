using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BasicApi.Features.Media;
using BasicApi.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace BasicApi.IntegrationTests.Features.Media;

/// <summary>Uploads straight to the storage by signed links, checks, previews, downloads (plan 2, F4.1).</summary>
public class MediaUploadTests(PostgresFixture db, StorageFixture storage) : DbTest(db)
{
    /// <summary>What a browser does with an upload link: a plain request, no API token.</summary>
    private static readonly HttpClient Storage = new();

    private ApiFactory Factory(Dictionary<string, string?>? extra = null)
    {
        var settings = storage.Settings();
        foreach (var (key, value) in extra ?? []) settings[key] = value;
        return new ApiFactory(Db.ConnectionString, settings);
    }

    public static byte[] Png(int width, int height, SKColor? color = null)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color ?? SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    public static async Task<JsonElement> StartAsync(HttpClient api, object request)
    {
        var response = await api.PostAsJsonAsync("/api/media/uploads", request);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadJsonAsync();
    }

    public static async Task<HttpResponseMessage> PutAsync(string url, byte[] content, string contentType)
    {
        using var body = new ByteArrayContent(content);
        body.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return await Storage.PutAsync(url, body);
    }

    /// <summary>The whole upload of one file: start, put, complete.</summary>
    public static async Task<JsonElement> UploadAsync(HttpClient api, string kind, byte[] content, string fileName,
        string mimeType, object? extra = null)
    {
        var request = new Dictionary<string, object?>
        {
            ["kind"] = kind, ["fileName"] = fileName, ["mimeType"] = mimeType, ["size"] = content.Length
        };
        foreach (var p in extra?.GetType().GetProperties() ?? [])
            request[p.Name] = p.GetValue(extra);
        var ticket = await StartAsync(api, request);
        var put = await PutAsync(ticket.GetProperty("uploadUrl").GetString()!, content, ticket.GetProperty("contentType").GetString()!);
        Assert.True(put.IsSuccessStatusCode, await put.Content.ReadAsStringAsync());
        return await api.PostJsonAsync($"/api/media/uploads/{ticket.Id("attachmentId")}/complete");
    }

    public static async Task<JsonElement?> LinkAsync(HttpClient api, Guid attachmentId)
    {
        var links = await api.PostJsonAsync("/api/media/links", new { attachmentIds = new[] { attachmentId } });
        return links.GetProperty("items").EnumerateArray().Select(i => (JsonElement?)i).SingleOrDefault();
    }

    [Fact]
    public async Task Photo_IsCheckedMeasuredAndPreviewed_AndServedForDisplay()
    {
        await using var factory = Factory();
        var alice = await factory.RegisterAsync("alice");
        using var api = factory.CreateClient(alice.Token);
        var png = Png(1600, 900);

        // The client claims a JPEG; the server believes the bytes.
        var photo = await UploadAsync(api, "photo", png, "C:\\Users\\alice\\beach.jpg", "image/jpeg");

        Assert.Equal("photo", photo.GetProperty("kind").GetString());
        Assert.Equal("beach.jpg", photo.GetProperty("fileName").GetString());
        Assert.Equal("image/png", photo.GetProperty("mimeType").GetString());
        Assert.Equal(png.Length, photo.GetProperty("size").GetInt64());
        Assert.Equal(1600, photo.GetProperty("width").GetInt32());
        Assert.Equal(900, photo.GetProperty("height").GetInt32());
        Assert.True(photo.GetProperty("hasThumbnail").GetBoolean());
        Assert.Equal("stored", photo.GetProperty("state").GetString());

        var link = (await LinkAsync(api, photo.Id()))!.Value;
        var original = await Storage.GetAsync(link.GetProperty("url").GetString());
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        Assert.Equal(png, await original.Content.ReadAsByteArrayAsync());
        Assert.Equal("image/png", original.Content.Headers.ContentType!.MediaType);
        Assert.Equal("inline", original.Content.Headers.ContentDisposition!.DispositionType);

        var thumbnail = await Storage.GetAsync(link.GetProperty("thumbnailUrl").GetString());
        Assert.Equal("image/jpeg", thumbnail.Content.Headers.ContentType!.MediaType);
        using var preview = SKBitmap.Decode(await thumbnail.Content.ReadAsByteArrayAsync());
        Assert.Equal((640, 360), (preview.Width, preview.Height));

        // The sha256 the server recorded is the uploaded file's.
        await using var connection = new Npgsql.NpgsqlConnection(Db.ConnectionString);
        var sha = await Dapper.SqlMapper.ExecuteScalarAsync<byte[]>(connection,
            "SELECT sha256 FROM attachments WHERE id = @id", new { id = photo.Id() });
        Assert.Equal(SHA256.HashData(png), sha);

        // Completing again changes nothing.
        var again = await api.PostJsonAsync($"/api/media/uploads/{photo.Id()}/complete");
        Assert.Equal(photo.GetRawText(), again.GetRawText());
    }

    [Fact]
    public async Task File_IsAlwaysADownload_SoUploadedHtmlNeverOpensAsAPage()
    {
        await using var factory = Factory();
        var alice = await factory.RegisterAsync("alice");
        using var api = factory.CreateClient(alice.Token);
        var html = Encoding.UTF8.GetBytes("<html><script>alert(document.cookie)</script></html>");

        var file = await UploadAsync(api, "file", html, "отчёт \"final\".html", "text/html");

        Assert.Equal("отчёт final.html", file.GetProperty("fileName").GetString());
        Assert.False(file.GetProperty("hasThumbnail").GetBoolean());
        var download = await Storage.GetAsync((await LinkAsync(api, file.Id()))!.Value.GetProperty("url").GetString());
        Assert.Equal("application/octet-stream", download.Content.Headers.ContentType!.MediaType);
        var disposition = download.Content.Headers.ContentDisposition!;
        Assert.Equal("attachment", disposition.DispositionType);
        Assert.Equal("отчёт final.html", disposition.FileNameStar);
    }

    [Fact]
    public async Task Photo_ThatIsNotAPicture_IsRefusedAndRemoved()
    {
        await using var factory = Factory();
        var alice = await factory.RegisterAsync("alice");
        using var api = factory.CreateClient(alice.Token);
        var fake = Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>");

        var ticket = await StartAsync(api, new { kind = "photo", fileName = "cat.svg", mimeType = "image/svg+xml", size = fake.Length });
        await PutAsync(ticket.GetProperty("uploadUrl").GetString()!, fake, "image/svg+xml");
        var complete = await api.PostAsync($"/api/media/uploads/{ticket.Id("attachmentId")}/complete", null);

        Assert.Equal(HttpStatusCode.BadRequest, complete.StatusCode);
        Assert.Equal("INVALID_MEDIA", await complete.ErrorCodeAsync());
        var retry = await api.PostAsync($"/api/media/uploads/{ticket.Id("attachmentId")}/complete", null);
        Assert.Equal("UPLOAD_NOT_FOUND", await retry.ErrorCodeAsync());
        Assert.Null(await factory.Services.GetRequiredService<IObjectStorage>()
            .GetSizeAsync(MediaService.OriginalKey(ticket.Id("attachmentId"))));
    }

    [Fact]
    public async Task Upload_IsCheckedAgainstTheLimits_BeforeAndAfter()
    {
        await using var factory = Factory(new() { ["Media:MaxFileSizeMb"] = "1" });
        var alice = await factory.RegisterAsync("alice");
        using var api = factory.CreateClient(alice.Token);

        var declared = await api.PostAsJsonAsync("/api/media/uploads",
            new { kind = "file", fileName = "big.bin", mimeType = "application/zip", size = 2 * 1024 * 1024 });
        Assert.Equal("FILE_TOO_LARGE", await declared.ErrorCodeAsync());

        // Declared small, uploaded big: the server measures the object itself.
        var ticket = await StartAsync(api, new { kind = "file", fileName = "big.bin", mimeType = "application/zip", size = 1000 });
        var early = await api.PostAsync($"/api/media/uploads/{ticket.Id("attachmentId")}/complete", null);
        Assert.Equal("UPLOAD_INCOMPLETE", await early.ErrorCodeAsync());
        await PutAsync(ticket.GetProperty("uploadUrl").GetString()!, new byte[1536 * 1024], "application/zip");
        var late = await api.PostAsync($"/api/media/uploads/{ticket.Id("attachmentId")}/complete", null);
        Assert.Equal("FILE_TOO_LARGE", await late.ErrorCodeAsync());

        var voice = await api.PostAsJsonAsync("/api/media/uploads",
            new { kind = "voice", fileName = "voice.ogg", mimeType = "audio/ogg", size = 100 });
        Assert.Equal("INVALID_MEDIA", await voice.ErrorCodeAsync());
        var sticker = await api.PostAsJsonAsync("/api/media/uploads",
            new { kind = "sticker", fileName = "x.webp", mimeType = "image/webp", size = 100 });
        Assert.Equal("INVALID_MEDIA", await sticker.ErrorCodeAsync());
    }

    [Fact]
    public async Task UploadLink_TakesOnlyTheAnnouncedContentType()
    {
        await using var factory = Factory();
        var alice = await factory.RegisterAsync("alice");
        using var api = factory.CreateClient(alice.Token);

        var ticket = await StartAsync(api, new { kind = "file", fileName = "a.txt", mimeType = "text/plain", size = 5 });
        var put = await PutAsync(ticket.GetProperty("uploadUrl").GetString()!, "hello"u8.ToArray(), "text/html");

        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
    }

    [Fact]
    public async Task OthersFiles_AreNeitherLinkedNorCompleted()
    {
        await using var factory = Factory();
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        var photo = await UploadAsync(aliceApi, "photo", Png(10, 10), "a.png", "image/png");
        var pending = await StartAsync(aliceApi, new { kind = "file", fileName = "b.txt", mimeType = "text/plain", size = 5 });

        Assert.Null(await LinkAsync(bobApi, photo.Id()));
        Assert.Equal(HttpStatusCode.NotFound, (await bobApi.GetAsync($"/api/media/{photo.Id()}")).StatusCode);
        var steal = await bobApi.PostAsync($"/api/media/uploads/{pending.Id("attachmentId")}/complete", null);
        Assert.Equal("UPLOAD_NOT_FOUND", await steal.ErrorCodeAsync());
        // A pending upload has nothing to download, even for its owner.
        Assert.Null(await LinkAsync(aliceApi, pending.Id("attachmentId")));

        var redirect = await factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
            .SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/media/{photo.Id()}?thumbnail=true")
            {
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", alice.Token) }
            });
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.StartsWith(storage.Endpoint, redirect.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Video_GetsItsPreviewFromTheFrameTheClientSent()
    {
        await using var factory = Factory();
        var alice = await factory.RegisterAsync("alice");
        using var api = factory.CreateClient(alice.Token);
        // An MP4 is recognised by its ftyp box; the rest of the file is not looked at.
        var mp4 = new byte[4096];
        "\0\0\0\u0018ftypisom"u8.ToArray().CopyTo(mp4, 0);

        var ticket = await StartAsync(api, new
        {
            kind = "video", fileName = "clip.mp4", mimeType = "video/mp4", size = mp4.Length,
            width = 1280, height = 720, durationMs = 5000, withThumbnail = true
        });
        await PutAsync(ticket.GetProperty("uploadUrl").GetString()!, mp4, "video/mp4");
        await PutAsync(ticket.GetProperty("thumbnailUploadUrl").GetString()!, Png(1280, 720), ticket.GetProperty("thumbnailContentType").GetString()!);
        var video = await api.PostJsonAsync($"/api/media/uploads/{ticket.Id("attachmentId")}/complete");

        Assert.Equal("video/mp4", video.GetProperty("mimeType").GetString());
        Assert.Equal(5000, video.GetProperty("durationMs").GetInt32());
        Assert.True(video.GetProperty("hasThumbnail").GetBoolean());
        var thumbnail = await Storage.GetAsync((await LinkAsync(api, video.Id()))!.Value.GetProperty("thumbnailUrl").GetString());
        using var preview = SKBitmap.Decode(await thumbnail.Content.ReadAsByteArrayAsync());
        Assert.Equal(640, preview.Width);
    }

    [Fact]
    public async Task Voice_KeepsItsLengthAndWaveform()
    {
        await using var factory = Factory();
        var alice = await factory.RegisterAsync("alice");
        using var api = factory.CreateClient(alice.Token);
        var ogg = new byte[512];
        "OggS"u8.ToArray().CopyTo(ogg, 0);

        var voice = await UploadAsync(api, "voice", ogg, "voice.ogg", "audio/ogg",
            new { durationMs = 3200, waveform = new[] { 0, 10, 255, 31 } });

        Assert.Equal("audio/ogg", voice.GetProperty("mimeType").GetString());
        Assert.Equal(3200, voice.GetProperty("durationMs").GetInt32());
        Assert.Equal([0, 10, 255, 31], voice.GetProperty("waveform").EnumerateArray().Select(v => v.GetInt32()));
    }

    [Fact]
    public async Task StaleUploads_AreSwept_AndThereIsACapOnUnfinishedOnes()
    {
        await using var factory = Factory(new() { ["Media:MaxPendingUploads"] = "2", ["Media:PendingUploadHours"] = "0" });
        var alice = await factory.RegisterAsync("alice");
        using var api = factory.CreateClient(alice.Token);
        var done = await UploadAsync(api, "file", "kept"u8.ToArray(), "c.txt", "text/plain");
        var first = await StartAsync(api, new { kind = "file", fileName = "a.txt", mimeType = "text/plain", size = 5 });
        await PutAsync(first.GetProperty("uploadUrl").GetString()!, "hello"u8.ToArray(), "text/plain");
        await StartAsync(api, new { kind = "file", fileName = "b.txt", mimeType = "text/plain", size = 5 });

        var third = await api.PostAsJsonAsync("/api/media/uploads", new { kind = "file", fileName = "d.txt", mimeType = "text/plain", size = 5 });
        Assert.Equal("TOO_MANY_UPLOADS", await third.ErrorCodeAsync());

        Assert.Equal(2, (await factory.Services.GetRequiredService<MediaCleanup>().CleanupAsync()).StaleUploads);
        var objects = factory.Services.GetRequiredService<IObjectStorage>();
        Assert.Null(await objects.GetSizeAsync(MediaService.OriginalKey(first.Id("attachmentId"))));
        Assert.NotNull(await objects.GetSizeAsync(MediaService.OriginalKey(done.Id())));
        await StartAsync(api, new { kind = "file", fileName = "d.txt", mimeType = "text/plain", size = 5 });
    }

    [Fact]
    public async Task WithoutStorage_MediaAnswersUnavailable_AndTheRestWorks()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        var alice = await factory.RegisterAsync("alice");
        using var api = factory.CreateClient(alice.Token);

        var response = await api.PostAsJsonAsync("/api/media/uploads", new { kind = "file", fileName = "a.txt", mimeType = "text/plain", size = 5 });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("MEDIA_UNAVAILABLE", await response.ErrorCodeAsync());
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("/api/chats")).StatusCode);
    }
}
