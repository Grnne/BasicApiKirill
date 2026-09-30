using System.Net;
using System.Net.Http.Json;
using BasicApi.Features.Media;
using BasicApi.IntegrationTests.Infrastructure;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static BasicApi.IntegrationTests.Features.Media.MediaUploadTests;

namespace BasicApi.IntegrationTests.Features.Media;

/// <summary>Unused files are removed; the retention policy keeps previews only (plan 2, F4.5, D2).</summary>
public class MediaRetentionTests(PostgresFixture db, StorageFixture storage) : DbTest(db)
{
    private ApiFactory Factory(Dictionary<string, string?> extra)
    {
        var settings = storage.Settings();
        foreach (var (key, value) in extra) settings[key] = value;
        return new ApiFactory(Db.ConnectionString, settings);
    }

    private async Task AgeAsync(Guid attachmentId, TimeSpan age)
    {
        await using var connection = new NpgsqlConnection(Db.ConnectionString);
        await connection.ExecuteAsync("UPDATE attachments SET stored_at = now() - @age WHERE id = @attachmentId",
            new { attachmentId, age });
    }

    [Fact]
    public async Task FilesNothingPointsTo_AreRemoved_OthersStay()
    {
        await using var factory = Factory(new() { ["Media:UnusedFileHours"] = "1" });
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var aliceApi = factory.CreateClient(alice.Token);
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        var objects = factory.Services.GetRequiredService<IObjectStorage>();

        var neverSent = (await UploadAsync(aliceApi, "photo", Png(8, 8), "a.png", "image/png")).Id();
        var takenBack = (await UploadAsync(aliceApi, "photo", Png(8, 8), "b.png", "image/png")).Id();
        var kept = (await UploadAsync(aliceApi, "photo", Png(8, 8), "c.png", "image/png")).Id();
        var avatar = (await UploadAsync(aliceApi, "photo", Png(8, 8), "d.png", "image/png")).Id();
        var fresh = (await UploadAsync(aliceApi, "file", "new"u8.ToArray(), "e.txt", "text/plain")).Id();
        var deleted = (await aliceApi.PostJsonAsync($"/api/chats/{chat}/messages", new { attachmentIds = new[] { takenBack } })).Id();
        await aliceApi.PostJsonAsync($"/api/chats/{chat}/messages", new { attachmentIds = new[] { kept } });
        await aliceApi.PutAsJsonAsync("/api/users/me/avatar", new { attachmentId = avatar });
        await aliceApi.DeleteAsync($"/api/chats/{chat}/messages/{deleted}?forEveryone=true");
        foreach (var id in new[] { neverSent, takenBack, kept, avatar })
            await AgeAsync(id, TimeSpan.FromHours(2));

        var result = await factory.Services.GetRequiredService<MediaCleanup>().CleanupAsync();

        Assert.Equal(2, result.UnusedFiles);
        foreach (var gone in new[] { neverSent, takenBack })
        {
            Assert.Null(await LinkAsync(aliceApi, gone));
            Assert.Null(await objects.GetSizeAsync(MediaService.OriginalKey(gone)));
            Assert.Null(await objects.GetSizeAsync(MediaService.ThumbnailKey(gone)));
        }
        foreach (var stays in new[] { kept, avatar, fresh })
            Assert.NotNull(await LinkAsync(aliceApi, stays));
    }

    [Fact]
    public async Task Retention_RemovesOldOriginals_KeepsPreviewsAndAvatars()
    {
        await using var factory = Factory(new() { ["Media:RetentionDays"] = "30" });
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        var objects = factory.Services.GetRequiredService<IObjectStorage>();
        var old = (await UploadAsync(aliceApi, "photo", Png(8, 8), "old.png", "image/png")).Id();
        var recent = (await UploadAsync(aliceApi, "photo", Png(8, 8), "new.png", "image/png")).Id();
        var avatar = (await UploadAsync(aliceApi, "photo", Png(8, 8), "me.png", "image/png")).Id();
        await aliceApi.PostJsonAsync($"/api/chats/{chat}/messages", new { attachmentIds = new[] { old, recent } });
        await aliceApi.PutAsJsonAsync("/api/users/me/avatar", new { attachmentId = avatar });
        await AgeAsync(old, TimeSpan.FromDays(31));
        await AgeAsync(avatar, TimeSpan.FromDays(31));

        var result = await factory.Services.GetRequiredService<MediaCleanup>().CleanupAsync();

        Assert.Equal(1, result.ExpiredOriginals);
        var link = (await LinkAsync(bobApi, old))!.Value;
        Assert.Equal(System.Text.Json.JsonValueKind.Null, link.GetProperty("url").ValueKind);
        Assert.Equal(HttpStatusCode.OK, (await new HttpClient().GetAsync(link.GetProperty("thumbnailUrl").GetString())).StatusCode);
        Assert.Null(await objects.GetSizeAsync(MediaService.OriginalKey(old)));
        Assert.NotNull(await objects.GetSizeAsync(MediaService.OriginalKey(avatar)));
        Assert.NotNull((await LinkAsync(bobApi, recent))!.Value.GetProperty("url").GetString());

        var shown = (await bobApi.HistoryAsync(chat))[0].GetProperty("attachments");
        Assert.Equal("expired", shown[0].GetProperty("state").GetString());
        Assert.Equal("stored", shown[1].GetProperty("state").GetString());
        // An expired file cannot be sent anew — there is nothing to send — but its message stays.
        var resend = await aliceApi.PostAsJsonAsync($"/api/chats/{chat}/messages", new { attachmentIds = new[] { old } });
        Assert.Equal("ATTACHMENT_NOT_FOUND", await resend.ErrorCodeAsync());
        // A second pass finds nothing more to expire.
        Assert.Equal(0, (await factory.Services.GetRequiredService<MediaCleanup>().CleanupAsync()).ExpiredOriginals);
    }
}
