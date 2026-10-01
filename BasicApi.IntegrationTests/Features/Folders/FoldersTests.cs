using System.Net;
using System.Net.Http.Json;
using BasicApi.IntegrationTests.Infrastructure;

namespace BasicApi.IntegrationTests.Features.Folders;

/// <summary>Folders of chats.</summary>
public class FoldersTests(PostgresFixture db) : DbTest(db)
{
    private static Dictionary<string, string?> Settings => new() { ["RateLimiting:CommandsPer10Seconds"] = "1000" };

    private static async Task<List<Guid>> ChatsAsync(HttpClient api, Guid folderId, string query = "") =>
        [.. (await api.GetJsonAsync($"/api/folders/{folderId}/chats?limit=50{query}")).GetProperty("items")
            .EnumerateArray().Select(c => c.Id("chatId"))];

    [Fact]
    public async Task Folder_ShowsItsChats_PinnedFirst_ThenByActivity()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        using var api = factory.CreateClient(alice.Token);
        var users = new List<Guid>();
        for (var i = 0; i < 4; i++) users.Add(await Data.UserAsync($"u{i}"));
        var listed = await Data.PrivateChatAsync(alice.UserId, users[0], TestData.T0.AddMinutes(1));
        var pinnedChat = await Data.PrivateChatAsync(alice.UserId, users[1], TestData.T0.AddMinutes(2));
        var other = await Data.PrivateChatAsync(alice.UserId, users[2], TestData.T0.AddMinutes(3));
        var group = await Data.GroupChatAsync("team", [alice.UserId, users[3]], TestData.T0.AddMinutes(4));
        var archivedGroup = await Data.GroupChatAsync("old", [alice.UserId, users[3]], TestData.T0.AddMinutes(5));
        await api.PutAsJsonAsync($"/api/chats/{archivedGroup}/archived", new { archived = true });

        var created = await api.PostAsJsonAsync("/api/folders", new
        {
            title = " Work ",
            includeGroups = true,
            chatIds = new[] { listed },
            pinnedChatIds = new[] { pinnedChat }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var folder = await created.ReadJsonAsync();
        Assert.Equal("Work", folder.GetProperty("title").GetString());
        Assert.Equal([pinnedChat], folder.GetProperty("pinnedChatIds").EnumerateArray().Select(c => c.GetGuid()));

        // Pinned first; then listed and all groups by activity; not the other private chat, not the archive.
        Assert.Equal([pinnedChat, group, listed], await ChatsAsync(api, folder.Id()));

        var first = await api.GetJsonAsync($"/api/folders/{folder.Id()}/chats?limit=1");
        Assert.Equal([pinnedChat, group], first.GetProperty("items").EnumerateArray().Select(c => c.Id("chatId")));
        Assert.Equal([listed], await ChatsAsync(api, folder.Id(), $"&cursor={first.GetProperty("nextCursor").GetString()}"));

        // A listed chat is shown even from the archive.
        await api.PatchAsJsonAsync($"/api/folders/{folder.Id()}", new { chatIds = new[] { listed, archivedGroup }, includeGroups = false });
        Assert.Equal([pinnedChat, archivedGroup, listed], await ChatsAsync(api, folder.Id()));
        Assert.DoesNotContain(other, await ChatsAsync(api, folder.Id()));

        var changes = await api.JournalAsync("FoldersChanged");
        Assert.Equal(2, changes.Count);
        Assert.Single(changes[^1].GetProperty("folders").EnumerateArray());
    }

    [Fact]
    public async Task OnlyUnread_ShowsChatsWithSomethingToRead()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        var withBob = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        var quiet = await Data.PrivateChatAsync(alice.UserId, await Data.UserAsync("carl"));
        var marked = await Data.PrivateChatAsync(alice.UserId, await Data.UserAsync("dave"));
        await bobApi.PostJsonAsync($"/api/chats/{withBob}/messages", new { text = "unread" });
        await aliceApi.PutAsJsonAsync($"/api/chats/{marked}/marked-unread", new { markedUnread = true });

        var folder = (await aliceApi.PostJsonAsync("/api/folders", new { title = "Unread", includePrivate = true, onlyUnread = true })).Id();

        var shown = await ChatsAsync(aliceApi, folder);
        Assert.Contains(withBob, shown);
        Assert.Contains(marked, shown);
        Assert.DoesNotContain(quiet, shown);
    }

    [Fact]
    public async Task Folders_HaveLimits_AnOrder_AndBelongToTheirOwner()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        var foreign = await Data.PrivateChatAsync(bob.UserId, await Data.UserAsync("carl"));

        async Task<string?> Code(Task<HttpResponseMessage> call) => await (await call).ErrorCodeAsync();

        Assert.Equal("INVALID_TITLE", await Code(aliceApi.PostAsJsonAsync("/api/folders", new { title = "  " })));
        Assert.Equal("INVALID_REQUEST", await Code(aliceApi.PostAsJsonAsync("/api/folders", new { title = "x", chatIds = new[] { foreign } })));

        var ids = new List<Guid>();
        for (var i = 0; i < 20; i++)
            ids.Add((await aliceApi.PostJsonAsync("/api/folders", new { title = $"f{i}" })).Id());
        Assert.Equal("TOO_MANY_FOLDERS", await Code(aliceApi.PostAsJsonAsync("/api/folders", new { title = "one more" })));

        Assert.Equal("INVALID_REQUEST", await Code(aliceApi.PutAsJsonAsync("/api/folders/order", new { folderIds = ids.Take(3) })));
        ids.Reverse();
        var reordered = await (await aliceApi.PutAsJsonAsync("/api/folders/order", new { folderIds = ids })).ReadJsonAsync();
        Assert.Equal(ids, reordered.EnumerateArray().Select(f => f.Id()));
        Assert.Equal(ids, (await aliceApi.GetJsonAsync("/api/folders")).EnumerateArray().Select(f => f.Id()));

        Assert.Equal("FOLDER_NOT_FOUND", await Code(bobApi.GetAsync($"/api/folders/{ids[0]}/chats")));
        Assert.Equal("FOLDER_NOT_FOUND", await Code(bobApi.DeleteAsync($"/api/folders/{ids[0]}")));
        Assert.Equal(HttpStatusCode.NoContent, (await aliceApi.DeleteAsync($"/api/folders/{ids[0]}")).StatusCode);
        Assert.Equal(19, (await aliceApi.GetJsonAsync("/api/folders")).GetArrayLength());
    }
}
