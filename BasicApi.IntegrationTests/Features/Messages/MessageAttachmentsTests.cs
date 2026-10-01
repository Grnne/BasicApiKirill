using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;
using static BasicApi.IntegrationTests.Features.Media.MediaUploadTests;

namespace BasicApi.IntegrationTests.Features.Messages;

/// <summary>Messages with files: albums, forwarding without a new upload, access through chats.</summary>
public class MessageAttachmentsTests(PostgresFixture db, StorageFixture storage) : DbTest(db)
{
    private ApiFactory Factory() => new(Db.ConnectionString, storage.Settings());

    private static Task<HttpResponseMessage> SendAsync(HttpClient api, Guid chatId, object body) =>
        api.PostAsJsonAsync($"/api/chats/{chatId}/messages", body);

    private static async Task<List<Guid>> LinkedAsync(HttpClient api, params Guid[] ids) =>
        [.. (await api.PostJsonAsync("/api/media/links", new { attachmentIds = ids }))
            .GetProperty("items").EnumerateArray().Select(i => i.Id("attachmentId"))];

    private static List<Guid> FileIds(JsonElement message) =>
        [.. message.GetProperty("attachments").EnumerateArray().Select(a => a.Id())];

    [Fact]
    public async Task Album_ReachesTheChat_InOrder_AndOnlyItsMembersCanOpenIt()
    {
        await using var factory = Factory();
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carl = await factory.RegisterAsync("carl");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        using var carlApi = factory.CreateClient(carl.Token);
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        var first = (await UploadAsync(aliceApi, "photo", Png(40, 30), "1.png", "image/png")).Id();
        var second = (await UploadAsync(aliceApi, "photo", Png(30, 40), "2.png", "image/png")).Id();

        // No caption at all: files alone make a message.
        var response = await SendAsync(aliceApi, chat, new { attachmentIds = new[] { second, first } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var sent = await response.ReadJsonAsync();
        Assert.Equal("media", sent.GetProperty("type").GetString());
        Assert.Equal("", sent.GetProperty("text").GetString());
        Assert.Equal([second, first], FileIds(sent));
        Assert.Equal(30, sent.GetProperty("attachments")[0].GetProperty("width").GetInt32());

        var seen = Assert.Single(await bobApi.HistoryAsync(chat));
        Assert.Equal([second, first], FileIds(seen));
        var created = Assert.Single(await bobApi.JournalAsync("MessageCreated"));
        Assert.Equal([second, first], FileIds(created));
        var last = (await bobApi.ChatItemAsync(chat)).GetProperty("lastMessage");
        Assert.Equal("media", last.GetProperty("type").GetString());
        Assert.Equal(2, last.GetProperty("attachments").GetArrayLength());

        Assert.Equal(2, (await LinkedAsync(bobApi, first, second)).Count);
        Assert.Empty(await LinkedAsync(carlApi, first, second));
    }

    [Fact]
    public async Task Forward_PointsToTheSameFiles_AndOpensThemToTheNewChat()
    {
        await using var factory = Factory();
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carl = await factory.RegisterAsync("carl");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        using var carlApi = factory.CreateClient(carl.Token);
        var withBob = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        var bobWithCarl = await Data.PrivateChatAsync(bob.UserId, carl.UserId);
        var photo = (await UploadAsync(aliceApi, "photo", Png(20, 20), "p.png", "image/png")).Id();
        var original = (await aliceApi.PostJsonAsync($"/api/chats/{withBob}/messages",
            new { text = "look", attachmentIds = new[] { photo } })).Id();

        var forwarded = await bobApi.PostJsonAsync($"/api/chats/{bobWithCarl}/messages/forward",
            new { fromChatId = withBob, messageIds = new[] { original } });

        var copy = forwarded.GetProperty("items")[0];
        Assert.Equal("media", copy.GetProperty("type").GetString());
        Assert.Equal("look", copy.GetProperty("text").GetString());
        Assert.Equal([photo], FileIds(copy));
        Assert.Equal([photo], FileIds(Assert.Single(await carlApi.HistoryAsync(bobWithCarl))));
        Assert.Equal([photo], await LinkedAsync(carlApi, photo));

        // Bob may also send the photo anew: he sees it in his chat, so no upload is needed.
        var again = await SendAsync(bobApi, bobWithCarl, new { attachmentIds = new[] { photo } });
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }

    [Fact]
    public async Task DeleteForEveryone_TakesTheFilesAwayFromTheChat()
    {
        await using var factory = Factory();
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        var file = (await UploadAsync(aliceApi, "file", "secret plan"u8.ToArray(), "plan.txt", "text/plain")).Id();
        var message = (await aliceApi.PostJsonAsync($"/api/chats/{chat}/messages", new { attachmentIds = new[] { file } })).Id();
        Assert.Equal([file], await LinkedAsync(bobApi, file));

        Assert.Equal(HttpStatusCode.NoContent,
            (await aliceApi.DeleteAsync($"/api/chats/{chat}/messages/{message}?forEveryone=true")).StatusCode);

        Assert.Empty(await LinkedAsync(bobApi, file));
        Assert.Equal([file], await LinkedAsync(aliceApi, file)); // still hers
        var reuse = await SendAsync(bobApi, chat, new { attachmentIds = new[] { file } });
        Assert.Equal("ATTACHMENT_NOT_FOUND", await reuse.ErrorCodeAsync());
    }

    [Fact]
    public async Task Albums_FollowTheRules()
    {
        await using var factory = Factory();
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carl = await factory.RegisterAsync("carl");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var carlApi = factory.CreateClient(carl.Token);
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        var carlChat = await Data.PrivateChatAsync(carl.UserId, bob.UserId);
        var photo = (await UploadAsync(aliceApi, "photo", Png(10, 10), "p.png", "image/png")).Id();
        var file = (await UploadAsync(aliceApi, "file", "x"u8.ToArray(), "x.txt", "text/plain")).Id();
        var ogg = new byte[64];
        "OggS"u8.ToArray().CopyTo(ogg, 0);
        var voice1 = (await UploadAsync(aliceApi, "voice", ogg, "a.ogg", "audio/ogg", new { durationMs = 1000 })).Id();
        var voice2 = (await UploadAsync(aliceApi, "voice", ogg, "b.ogg", "audio/ogg", new { durationMs = 1000 })).Id();
        var pending = (await StartAsync(aliceApi, new { kind = "file", fileName = "p.txt", mimeType = "text/plain", size = 1 })).Id("attachmentId");

        async Task<string?> Code(HttpClient api, Guid chatId, object body) => await (await SendAsync(api, chatId, body)).ErrorCodeAsync();

        Assert.Equal("INVALID_ALBUM", await Code(aliceApi, chat, new { attachmentIds = Enumerable.Range(0, 11).Select(_ => Guid.NewGuid()) }));
        Assert.Equal("INVALID_ALBUM", await Code(aliceApi, chat, new { attachmentIds = new[] { photo, photo } }));
        Assert.Equal("INVALID_ALBUM", await Code(aliceApi, chat, new { attachmentIds = new[] { photo, file } }));
        Assert.Equal("INVALID_ALBUM", await Code(aliceApi, chat, new { attachmentIds = new[] { voice1, voice2 } }));
        Assert.Equal("ATTACHMENT_NOT_FOUND", await Code(aliceApi, chat, new { attachmentIds = new[] { pending } }));
        Assert.Equal("ATTACHMENT_NOT_FOUND", await Code(carlApi, carlChat, new { attachmentIds = new[] { photo } }));
        Assert.Equal("MESSAGE_EMPTY", await Code(aliceApi, chat, new { text = "  " }));

        Assert.Equal(HttpStatusCode.Created, (await SendAsync(aliceApi, chat, new { attachmentIds = new[] { voice1 } })).StatusCode);
    }

    [Fact]
    public async Task Caption_CanBeEditedAway_ButAText_CannotBeEmptied()
    {
        await using var factory = Factory();
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var aliceApi = factory.CreateClient(alice.Token);
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        var photo = (await UploadAsync(aliceApi, "photo", Png(10, 10), "p.png", "image/png")).Id();
        var media = (await aliceApi.PostJsonAsync($"/api/chats/{chat}/messages", new { text = "caption", attachmentIds = new[] { photo } })).Id();
        var text = (await aliceApi.PostJsonAsync($"/api/chats/{chat}/messages", new { text = "hello" })).Id();

        var edited = await aliceApi.PatchAsJsonAsync($"/api/chats/{chat}/messages/{media}", new { text = "" });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal("", (await edited.ReadJsonAsync()).GetProperty("text").GetString());
        Assert.Equal([photo], FileIds(await edited.ReadJsonAsync()));

        var emptied = await aliceApi.PatchAsJsonAsync($"/api/chats/{chat}/messages/{text}", new { text = " " });
        Assert.Equal("MESSAGE_EMPTY", await emptied.ErrorCodeAsync());

        // A media message can be answered like any other.
        Assert.Equal(HttpStatusCode.Created,
            (await SendAsync(aliceApi, chat, new { text = "nice", replyToMessageId = media })).StatusCode);
    }

    [Fact]
    public async Task GroupWithoutMedia_TakesTextButNotFiles_NorForwardedFiles()
    {
        await using var factory = Factory();
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        var group = await Data.GroupChatAsync("team", [alice.UserId, bob.UserId]);
        var privateChat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        Assert.Equal(HttpStatusCode.OK, (await aliceApi.PatchAsJsonAsync($"/api/chats/{group}",
            new { memberPermissions = new { sendMedia = false } })).StatusCode);
        var photo = (await UploadAsync(bobApi, "photo", Png(10, 10), "p.png", "image/png")).Id();
        var inPrivate = (await bobApi.PostJsonAsync($"/api/chats/{privateChat}/messages", new { attachmentIds = new[] { photo } })).Id();

        Assert.Equal("PERMISSION_DENIED", await (await SendAsync(bobApi, group, new { attachmentIds = new[] { photo } })).ErrorCodeAsync());
        var forward = await bobApi.PostAsJsonAsync($"/api/chats/{group}/messages/forward",
            new { fromChatId = privateChat, messageIds = new[] { inPrivate } });
        Assert.Equal("PERMISSION_DENIED", await forward.ErrorCodeAsync());
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(bobApi, group, new { text = "words only" })).StatusCode);
        // The owner is not bound by the members' defaults.
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(aliceApi, group, new { attachmentIds = new[] { photo } })).StatusCode);
    }
}
