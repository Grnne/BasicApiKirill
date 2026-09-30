using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;
using static BasicApi.IntegrationTests.Features.Media.MediaUploadTests;

namespace BasicApi.IntegrationTests.Features.Media;

/// <summary>Avatars of users and groups.</summary>
public class AvatarsTests(PostgresFixture db, StorageFixture storage) : DbTest(db)
{
    private static Guid? AvatarOf(JsonElement e) =>
        e.GetProperty("avatarId").ValueKind == JsonValueKind.Null ? null : e.GetProperty("avatarId").GetGuid();

    private static async Task<List<Guid>> LinkedAsync(HttpClient api, Guid id) =>
        [.. (await api.PostJsonAsync("/api/media/links", new { attachmentIds = new[] { id } }))
            .GetProperty("items").EnumerateArray().Select(i => i.Id("attachmentId"))];

    [Fact]
    public async Task UserAvatar_ShowsEverywhere_AndContactsHearOfIt()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, storage.Settings());
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carl = await factory.RegisterAsync("carl");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        using var carlApi = factory.CreateClient(carl.Token);
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        var photo = (await UploadAsync(aliceApi, "photo", Png(64, 64), "me.png", "image/png")).Id();

        var set = await aliceApi.PutAsJsonAsync("/api/users/me/avatar", new { attachmentId = photo });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal(photo, AvatarOf(await set.ReadJsonAsync()));
        // The same photo again is no change: nobody hears of it twice.
        await aliceApi.PutAsJsonAsync("/api/users/me/avatar", new { attachmentId = photo });

        var update = Assert.Single(await bobApi.JournalAsync("UserUpdated"));
        Assert.Equal(alice.UserId, update.Id("userId"));
        Assert.Equal(photo, AvatarOf(update));
        Assert.Single(await aliceApi.JournalAsync("UserUpdated")); // her other devices
        Assert.Empty(await carlApi.JournalAsync("UserUpdated"));

        Assert.Equal(photo, AvatarOf(await bobApi.ChatItemAsync(chat)));
        Assert.Equal(photo, AvatarOf(await carlApi.GetJsonAsync($"/api/users/{alice.UserId}")));
        Assert.Equal(photo, AvatarOf(await aliceApi.GetJsonAsync("/api/users/me")));
        var details = await bobApi.GetJsonAsync($"/api/chats/{chat}");
        Assert.Equal(photo, AvatarOf(details));
        Assert.Equal(photo, AvatarOf(details.GetProperty("participants").EnumerateArray().Single(p => p.Id("userId") == alice.UserId)));
        var found = (await carlApi.GetJsonAsync("/api/users/search?q=alice")).GetProperty("items")[0];
        Assert.Equal(photo, AvatarOf(found));
        // Anyone signed in sees a user's avatar — not only those who share a chat.
        Assert.Equal([photo], await LinkedAsync(carlApi, photo));

        var removed = await aliceApi.DeleteAsync("/api/users/me/avatar");
        Assert.Null(AvatarOf(await removed.ReadJsonAsync()));
        Assert.Null(AvatarOf((await bobApi.JournalAsync("UserUpdated"))[^1]));
        Assert.Empty(await LinkedAsync(carlApi, photo));
    }

    [Fact]
    public async Task Avatar_IsAPhotoOfYourOwn()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, storage.Settings());
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        var file = (await UploadAsync(aliceApi, "file", "x"u8.ToArray(), "x.png", "image/png")).Id();
        var bobs = (await UploadAsync(bobApi, "photo", Png(8, 8), "b.png", "image/png")).Id();
        await bobApi.PostJsonAsync($"/api/chats/{chat}/messages", new { attachmentIds = new[] { bobs } });

        Assert.Equal("INVALID_AVATAR", await (await aliceApi.PutAsJsonAsync("/api/users/me/avatar", new { attachmentId = file })).ErrorCodeAsync());
        // Seen in a chat is not enough: an avatar is chosen from one's own uploads.
        Assert.Equal("ATTACHMENT_NOT_FOUND", await (await aliceApi.PutAsJsonAsync("/api/users/me/avatar", new { attachmentId = bobs })).ErrorCodeAsync());
        Assert.Equal("ATTACHMENT_NOT_FOUND", await (await aliceApi.PutAsJsonAsync("/api/users/me/avatar", new { attachmentId = Guid.NewGuid() })).ErrorCodeAsync());
    }

    [Fact]
    public async Task GroupPhoto_NeedsTheRight_TellsTheMembers_AndIsTheirsToSee()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, storage.Settings());
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carl = await factory.RegisterAsync("carl");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        using var carlApi = factory.CreateClient(carl.Token);
        var group = await Data.GroupChatAsync("team", [alice.UserId, bob.UserId]);
        var privateChat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        var photo = (await UploadAsync(aliceApi, "photo", Png(64, 64), "team.png", "image/png")).Id();
        var bobs = (await UploadAsync(bobApi, "photo", Png(64, 64), "mine.png", "image/png")).Id();

        Assert.Equal("PERMISSION_DENIED", await (await bobApi.PutAsJsonAsync($"/api/chats/{group}/avatar", new { attachmentId = bobs })).ErrorCodeAsync());
        Assert.Equal("NOT_A_GROUP", await (await aliceApi.PutAsJsonAsync($"/api/chats/{privateChat}/avatar", new { attachmentId = photo })).ErrorCodeAsync());

        var set = await aliceApi.PutAsJsonAsync($"/api/chats/{group}/avatar", new { attachmentId = photo });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal(photo, AvatarOf(await set.ReadJsonAsync()));

        var updated = Assert.Single(await bobApi.JournalAsync("ChatUpdated"));
        Assert.Equal(photo, AvatarOf(updated));
        Assert.Equal("team", updated.GetProperty("title").GetString());
        var system = (await bobApi.HistoryAsync(group))[^1];
        Assert.Equal("photo_changed", system.GetProperty("action").GetProperty("type").GetString());
        Assert.Equal("Фото группы изменено", system.GetProperty("text").GetString());
        Assert.Equal(photo, AvatarOf(await bobApi.ChatItemAsync(group)));
        Assert.Equal(photo, AvatarOf(await bobApi.GetJsonAsync($"/api/chats/{group}")));
        Assert.Equal([photo], await LinkedAsync(bobApi, photo));
        Assert.Empty(await LinkedAsync(carlApi, photo));

        var audit = (await aliceApi.GetJsonAsync($"/api/chats/{group}/audit")).GetProperty("items")[0];
        Assert.Equal("photo_changed", audit.GetProperty("action").GetString());

        var removed = await aliceApi.DeleteAsync($"/api/chats/{group}/avatar");
        Assert.Null(AvatarOf(await removed.ReadJsonAsync()));
        Assert.Equal("photo_removed", (await bobApi.HistoryAsync(group))[^1].GetProperty("action").GetProperty("type").GetString());
        Assert.Null(AvatarOf(await bobApi.ChatItemAsync(group)));
    }
}
