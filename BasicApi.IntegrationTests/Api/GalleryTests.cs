using System.Net;
using System.Net.Http.Json;
using BasicApi.IntegrationTests.Infrastructure;
using static BasicApi.IntegrationTests.Features.Media.MediaUploadTests;

namespace BasicApi.IntegrationTests.Api;

/// <summary>A chat's gallery: photos and videos, files, voice, links (plan 2, F4.3).</summary>
public class GalleryTests(PostgresFixture db, StorageFixture storage) : DbTest(db)
{
    private static async Task<Guid> SendAsync(HttpClient api, Guid chatId, object body) =>
        (await api.PostJsonAsync($"/api/chats/{chatId}/messages", body)).Id();

    private static async Task<List<Guid>> TabAsync(HttpClient api, Guid chatId, string filter) =>
        [.. (await api.GetJsonAsync($"/api/chats/{chatId}/media?filter={filter}")).GetProperty("items")
            .EnumerateArray().Select(m => m.Id())];

    [Fact]
    public async Task EachTab_ShowsItsMessages_NewestFirst()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, storage.Settings());
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        var ogg = new byte[64];
        "OggS"u8.ToArray().CopyTo(ogg, 0);
        var mp4 = new byte[64];
        "\0\0\0\u0018ftypisom"u8.ToArray().CopyTo(mp4, 0);

        async Task<Guid> Upload(string kind, byte[] bytes, string name, string mime, object? extra = null) =>
            (await UploadAsync(aliceApi, kind, bytes, name, mime, extra)).Id();

        var album = await SendAsync(aliceApi, chat, new
        {
            attachmentIds = new[] { await Upload("photo", Png(8, 8), "a.png", "image/png"), await Upload("photo", Png(8, 8), "b.png", "image/png") }
        });
        await SendAsync(aliceApi, chat, new { text = "plain words" });
        var video = await SendAsync(aliceApi, chat, new
        {
            text = "clip", attachmentIds = new[] { await Upload("video", mp4, "c.mp4", "video/mp4", new { width = 16, height = 9 }) }
        });
        var file = await SendAsync(aliceApi, chat, new { attachmentIds = new[] { await Upload("file", "doc"u8.ToArray(), "d.txt", "text/plain") } });
        var voice = await SendAsync(aliceApi, chat, new
        {
            attachmentIds = new[] { await Upload("voice", ogg, "v.ogg", "audio/ogg", new { durationMs = 900 }) }
        });
        var address = await SendAsync(aliceApi, chat, new { text = "see HTTPS://example.com/page" });
        var formatted = await SendAsync(aliceApi, chat, new
        {
            text = "the docs", entities = new[] { new { type = "link", offset = 4, length = 4, url = "https://docs.example.com" } }
        });
        var later = await SendAsync(aliceApi, chat, new { text = "no address yet" });

        Assert.Equal([video, album], await TabAsync(bobApi, chat, "media"));
        Assert.Equal([file], await TabAsync(bobApi, chat, "files"));
        Assert.Equal([voice], await TabAsync(bobApi, chat, "voice"));
        Assert.Equal([formatted, address], await TabAsync(bobApi, chat, "links"));

        // An edit that adds an address puts the message in the tab; one deleted for oneself leaves it.
        Assert.Equal(HttpStatusCode.OK, (await aliceApi.PatchAsJsonAsync($"/api/chats/{chat}/messages/{later}",
            new { text = "now http://example.org" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await bobApi.DeleteAsync($"/api/chats/{chat}/messages/{album}")).StatusCode);
        Assert.Equal([later, formatted, address], await TabAsync(bobApi, chat, "links"));
        Assert.Equal([video], await TabAsync(bobApi, chat, "media"));
        Assert.Equal([video, album], await TabAsync(aliceApi, chat, "media"));

        // Page by page, with the files in each message.
        var first = await aliceApi.GetJsonAsync($"/api/chats/{chat}/media?filter=media&limit=1");
        Assert.True(first.GetProperty("hasMore").GetBoolean());
        Assert.Equal(1, first.GetProperty("items")[0].GetProperty("attachments").GetArrayLength());
        var second = await aliceApi.GetJsonAsync(
            $"/api/chats/{chat}/media?filter=media&limit=1&cursor={first.GetProperty("nextCursor").GetString()}");
        Assert.Equal(album, second.GetProperty("items")[0].Id());
        Assert.Equal(2, second.GetProperty("items")[0].GetProperty("attachments").GetArrayLength());
        Assert.False(second.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task Gallery_IsForMembers_WithAKnownTab()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, storage.Settings());
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carl = await factory.RegisterAsync("carl");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var carlApi = factory.CreateClient(carl.Token);
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);

        Assert.Equal("INVALID_FILTER", await (await aliceApi.GetAsync($"/api/chats/{chat}/media?filter=stickers")).ErrorCodeAsync());
        Assert.Equal("INVALID_FILTER", await (await aliceApi.GetAsync($"/api/chats/{chat}/media")).ErrorCodeAsync());
        Assert.Equal("NOT_A_MEMBER", await (await carlApi.GetAsync($"/api/chats/{chat}/media?filter=media")).ErrorCodeAsync());
        Assert.Empty((await aliceApi.GetJsonAsync($"/api/chats/{chat}/media?filter=links")).GetProperty("items").EnumerateArray());
    }
}
