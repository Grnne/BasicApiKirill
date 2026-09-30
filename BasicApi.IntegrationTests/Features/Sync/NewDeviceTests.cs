using System.Net.Http.Json;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;
using static BasicApi.IntegrationTests.Features.Media.MediaUploadTests;

namespace BasicApi.IntegrationTests.Features.Sync;

/// <summary>
/// Full history on any new device (plan 2, F8, D3). Device A is there from the start and keeps its
/// view the way a client does — from the snapshot and then the journal. Device B signs in after
/// everything the plan's functions can do has happened, and starts from scratch: the snapshot and
/// the history page by page. Both must see the same; so must B after catching up on what follows.
/// </summary>
public class NewDeviceTests(PostgresFixture db, StorageFixture storage) : DbTest(db)
{
    private ApiFactory Factory()
    {
        var settings = storage.Settings();
        settings["RateLimiting:CommandsPer10Seconds"] = "1000";
        settings["RateLimiting:PerUserPerMinute"] = "10000";
        return new ApiFactory(Db.ConnectionString, settings);
    }

    private static async Task<JsonElement> SnapshotAsync(HttpClient api) => await api.GetJsonAsync("/api/sync/state");

    /// <summary>A new device: the snapshot, then every chat's history back to its first message, three at a time.</summary>
    private static async Task<DeviceView> NewDeviceAsync(HttpClient api, Guid me)
    {
        var state = await SnapshotAsync(api);
        var histories = new Dictionary<Guid, List<JsonElement>>();
        foreach (var chat in state.GetProperty("chats").EnumerateArray())
        {
            var chatId = chat.Id("chatId");
            var messages = new List<JsonElement>();
            string? cursor = null;
            do
            {
                var page = await api.GetJsonAsync($"/api/chats/{chatId}/messages/cursor?limit=3{(cursor is null ? "" : $"&cursor={cursor}")}");
                messages.InsertRange(0, page.GetProperty("items").EnumerateArray());
                cursor = page.GetProperty("hasMore").GetBoolean() ? page.GetProperty("nextCursor").GetString() : null;
            } while (cursor is not null);
            histories[chatId] = messages;
        }
        return DeviceView.FromSnapshot(me, state, histories);
    }

    /// <summary>Applies everything after the device's pts, page by page, as a client does after a reconnect.</summary>
    private static async Task CatchUpAsync(HttpClient api, DeviceView device)
    {
        bool more;
        do
        {
            var diff = await api.GetJsonAsync($"/api/sync?since={device.Pts}&limit=7");
            Assert.False(diff.GetProperty("snapshotRequired").GetBoolean());
            foreach (var update in diff.GetProperty("updates").EnumerateArray())
                device.Apply(update);
            more = diff.GetProperty("hasMore").GetBoolean();
        } while (more);
    }

    private static async Task<Guid> SendAsync(HttpClient api, Guid chatId, object body) =>
        (await api.PostJsonAsync($"/api/chats/{chatId}/messages", body)).Id();

    private static Task<Guid> SendAsync(HttpClient api, Guid chatId, string text) => SendAsync(api, chatId, new { text });

    private static async Task<Guid> GroupAsync(HttpClient api, string title, params Guid[] members) =>
        (await api.PostJsonAsync("/api/chats/groups", new { title, memberIds = members })).Id("chatId");

    private static async Task OkAsync(Task<HttpResponseMessage> request)
    {
        var response = await request;
        Assert.True(response.IsSuccessStatusCode,
            $"{response.RequestMessage!.Method} {response.RequestMessage.RequestUri}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task NewDevice_SeesExactlyWhatTheOldOneSees()
    {
        await using var factory = Factory();
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carl = await factory.RegisterAsync("carl");
        var dave = await factory.RegisterAsync("dave");
        using var a = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        using var carlApi = factory.CreateClient(carl.Token);
        using var daveApi = factory.CreateClient(dave.Token);

        // Device A is there from the very start.
        var deviceA = await NewDeviceAsync(a, alice.UserId);

        // A private chat: replies, an edit, deletions for everyone and for oneself, reactions, a photo.
        var withBob = (await bobApi.PostJsonAsync($"/api/chats/private/{alice.UserId}")).Id("chatId");
        var b1 = await SendAsync(bobApi, withBob, "Привет, Алиса");
        var a1 = await SendAsync(a, withBob, new { text = "Привет!", replyToMessageId = b1 });
        var b2 = await SendAsync(bobApi, withBob, "как дела?");
        var a2 = await SendAsync(a, withBob, "отлично");
        await OkAsync(a.PatchAsJsonAsync($"/api/chats/{withBob}/messages/{a2}", new { text = "отлично, спасибо" }));
        var b3 = await SendAsync(bobApi, withBob, "удалю у всех");
        await OkAsync(bobApi.DeleteAsync($"/api/chats/{withBob}/messages/{b3}?forEveryone=true"));
        var b4 = await SendAsync(bobApi, withBob, "это Алиса скроет у себя");
        await OkAsync(a.DeleteAsync($"/api/chats/{withBob}/messages/{b4}"));
        await OkAsync(bobApi.PutAsJsonAsync($"/api/chats/{withBob}/messages/{a1}/reactions", new { emoji = "👍" }));
        await OkAsync(a.PutAsJsonAsync($"/api/chats/{withBob}/messages/{b2}/reactions", new { emoji = "❤️" }));
        await OkAsync(a.PutAsJsonAsync($"/api/chats/{withBob}/messages/{b2}/reactions", new { emoji = "🔥" }));
        var photo = (await UploadAsync(bobApi, "photo", Png(40, 30), "cat.png", "image/png")).Id();
        var b5 = await SendAsync(bobApi, withBob, new { text = "фото", attachmentIds = new[] { photo } });
        await OkAsync(a.PostAsJsonAsync($"/api/chats/{withBob}/read", new { lastMessageId = b2 }));
        await OkAsync(bobApi.PostAsJsonAsync($"/api/chats/{withBob}/read", new { lastMessageId = b5 }));
        await OkAsync(a.PutAsJsonAsync($"/api/chats/{withBob}/marked-unread", new { markedUnread = true }));

        // A group: a mention of Alice, renaming, roles, a member added and removed, a draft.
        var team = await GroupAsync(a, "Команда", bob.UserId, carl.UserId);
        await SendAsync(bobApi, team, new
        {
            text = "@alice посмотри",
            entities = new[] { new { type = "mention", offset = 0, length = 6, userId = alice.UserId } }
        });
        await SendAsync(carlApi, team, new { text = "**ок**", entities = new[] { new { type = "bold", offset = 2, length = 2 } } });
        await OkAsync(a.PatchAsJsonAsync($"/api/chats/{team}", new { title = "Команда 2" }));
        await OkAsync(a.PostAsJsonAsync($"/api/chats/{team}/members", new { userIds = new[] { dave.UserId } }));
        await OkAsync(a.PutAsJsonAsync($"/api/chats/{team}/members/{carl.UserId}/role", new { role = "admin" }));
        await OkAsync(a.DeleteAsync($"/api/chats/{team}/members/{dave.UserId}"));
        await OkAsync(a.PutAsJsonAsync($"/api/chats/{team}/draft", new { text = "черновик в группе" }));

        // Folders, one of them with a chat Alice will lose.
        var old = await GroupAsync(bobApi, "Старая", alice.UserId);
        await SendAsync(bobApi, old, "прощай");
        var work = (await a.PostJsonAsync("/api/folders", new
        {
            title = "Работа", includeGroups = true, chatIds = new[] { withBob }, pinnedChatIds = new[] { team }
        })).Id();
        var people = (await a.PostJsonAsync("/api/folders", new { title = "Люди", includePrivate = true, chatIds = new[] { old } })).Id();
        var temporary = (await a.PostJsonAsync("/api/folders", new { title = "Временная" })).Id();
        await OkAsync(a.DeleteAsync($"/api/folders/{temporary}"));
        await OkAsync(a.PatchAsJsonAsync($"/api/folders/{work}", new { title = "Работа 2" }));
        await OkAsync(a.PutAsJsonAsync("/api/folders/order", new { folderIds = new[] { people, work } }));

        // Chats Alice loses: removed from one, another deleted by its owner.
        await OkAsync(bobApi.DeleteAsync($"/api/chats/{old}/members/{alice.UserId}"));
        var gone = await GroupAsync(carlApi, "Удалённая", alice.UserId);
        await SendAsync(carlApi, gone, "скоро удалю");
        await OkAsync(carlApi.DeleteAsync($"/api/chats/{gone}"));

        // Saved messages with a forward.
        var saved = (await a.PostJsonAsync("/api/chats/saved")).Id("chatId");
        await OkAsync(a.PostAsJsonAsync($"/api/chats/{saved}/messages/forward", new { fromChatId = withBob, messageIds = new[] { b1, b5 } }));
        await SendAsync(a, saved, "заметка");

        // Pins, archive and mute.
        var withCarl = (await a.PostJsonAsync($"/api/chats/private/{carl.UserId}")).Id("chatId");
        await SendAsync(carlApi, withCarl, "привет от Карла");
        await OkAsync(a.PutAsJsonAsync($"/api/chats/{team}/pinned", new { pinned = true }));
        await OkAsync(a.PutAsJsonAsync($"/api/chats/{withBob}/pinned", new { pinned = true }));
        await OkAsync(a.PutAsJsonAsync($"/api/chats/{withCarl}/pinned", new { pinned = true }));
        await OkAsync(a.PutAsJsonAsync("/api/chats/pinned", new { chatIds = new[] { withBob, withCarl, team } }));
        await OkAsync(a.PutAsJsonAsync($"/api/chats/{withCarl}/archived", new { archived = true })); // unpins it
        var quiet = await GroupAsync(carlApi, "Тихая", alice.UserId);
        await OkAsync(a.PutAsJsonAsync($"/api/chats/{quiet}/muted", new { muted = true }));
        await OkAsync(a.PutAsJsonAsync($"/api/chats/{quiet}/archived", new { archived = true }));
        await SendAsync(carlApi, quiet, "в архиве и без звука — там и остаётся");
        var withDave = (await daveApi.PostJsonAsync($"/api/chats/private/{alice.UserId}")).Id("chatId");
        await SendAsync(daveApi, withDave, "раз");
        await OkAsync(a.PutAsJsonAsync($"/api/chats/{withDave}/archived", new { archived = true }));
        await SendAsync(daveApi, withDave, "два — чат возвращается из архива");
        await OkAsync(a.PutAsJsonAsync($"/api/chats/{withBob}/muted", new { muted = true, until = DateTime.UtcNow.AddHours(1) }));

        // A draft that sending clears.
        await OkAsync(a.PutAsJsonAsync($"/api/chats/{withBob}/draft", new { text = "ещё не отправлено", replyToMessageId = b2 }));
        await SendAsync(a, withBob, new { text = "отправлено", replyToMessageId = b2 });

        // Privacy, blocks, profiles, an avatar.
        await OkAsync(a.PutAsJsonAsync("/api/users/me/privacy", new { lastSeen = "nobody", messages = "contacts" }));
        await OkAsync(a.PutAsync($"/api/users/{dave.UserId}/block", null));
        await OkAsync(a.PutAsync($"/api/users/{carl.UserId}/block", null));
        await OkAsync(a.DeleteAsync($"/api/users/{carl.UserId}/block"));
        await OkAsync(a.PatchAsJsonAsync("/api/users/me", new { displayName = "Алиса" }));
        await OkAsync(bobApi.PatchAsJsonAsync("/api/users/me", new { displayName = "Боб" }));
        var avatar = (await UploadAsync(a, "photo", Png(64, 64), "me.png", "image/png")).Id();
        await OkAsync(a.PutAsJsonAsync("/api/users/me/avatar", new { attachmentId = avatar }));
        var teamPhoto = (await UploadAsync(a, "photo", Png(48, 48), "team.png", "image/png")).Id();
        await OkAsync(a.PutAsJsonAsync($"/api/chats/{team}/avatar", new { attachmentId = teamPhoto }));

        // Bob's device confirms what it received: Alice's last message becomes delivered.
        await OkAsync(bobApi.PostAsJsonAsync("/api/sync/ack", new { pts = await bobApi.PtsAsync() }));

        // Device A has followed the journal all along; device B signs in now.
        await CatchUpAsync(a, deviceA);
        var bSignIn = await factory.LoginAsync("alice");
        using var b = factory.CreateClient(bSignIn.Token);
        var deviceB = await NewDeviceAsync(b, alice.UserId);

        DeviceView.AssertSame(deviceA, deviceB);
        Assert.Equal(deviceA.Pts, deviceB.Pts);

        // Every file the new device shows opens there.
        var files = deviceB.Describe().SelectMany(l => System.Text.RegularExpressions.Regex.Matches(l, "\"([0-9a-f-]{36})/stored\"")
            .Select(m => Guid.Parse(m.Groups[1].Value))).Concat([avatar, teamPhoto]).Distinct().ToList();
        Assert.Contains(photo, files);
        var links = (await b.PostJsonAsync("/api/media/links", new { attachmentIds = files })).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(files.Order(), links.Select(l => l.Id("attachmentId")).Order());
        Assert.All(links, l => Assert.NotNull(l.GetProperty("url").GetString()));

        // Life goes on; B catches up from its snapshot and is still in step with A.
        await SendAsync(bobApi, withBob, "ещё одно");
        await OkAsync(bobApi.PutAsJsonAsync($"/api/chats/{withBob}/messages/{a2}/reactions", new { emoji = "😂" }));
        await OkAsync(a.PostAsJsonAsync($"/api/chats/{team}/read", new
        {
            lastMessageId = (await a.HistoryAsync(team)).Last().Id()
        }));
        await OkAsync(a.PutAsJsonAsync($"/api/chats/{withDave}/pinned", new { pinned = true }));
        await OkAsync(a.DeleteAsync($"/api/chats/{team}/draft"));
        await OkAsync(a.PutAsJsonAsync("/api/users/me/privacy", new { groupAdd = "nobody" }));
        await OkAsync(a.DeleteAsync($"/api/users/{dave.UserId}/block"));
        await OkAsync(a.DeleteAsync($"/api/folders/{people}"));
        await OkAsync(carlApi.PatchAsJsonAsync($"/api/chats/{team}", new { title = "Команда 3" }));

        await CatchUpAsync(a, deviceA);
        await CatchUpAsync(b, deviceB);
        DeviceView.AssertSame(deviceA, deviceB);
        DeviceView.AssertSame(deviceA, await NewDeviceAsync(b, alice.UserId));
    }
}
