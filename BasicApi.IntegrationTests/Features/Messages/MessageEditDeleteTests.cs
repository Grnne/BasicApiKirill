using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.SignalR.Client;

namespace BasicApi.IntegrationTests.Features.Messages;

/// <summary>Editing and deleting messages over REST, with events and sync.</summary>
public class MessageEditDeleteTests(PostgresFixture db) : DbTest(db)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private sealed record Arranged(
        ApiFactory Factory, AuthResult Alice, AuthResult Bob, HttpClient AliceApi, HttpClient BobApi, Guid Chat) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            AliceApi.Dispose();
            BobApi.Dispose();
            await Factory.DisposeAsync();
        }
    }

    private async Task<Arranged> ArrangeAsync()
    {
        var factory = new ApiFactory(Db.ConnectionString,
            new Dictionary<string, string?> { ["RateLimiting:CommandsPer10Seconds"] = "1000" });
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        return new Arranged(factory, alice, bob, factory.CreateClient(alice.Token), factory.CreateClient(bob.Token), chat);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private static async Task<Guid> SendAsync(HttpClient client, Guid chat, string text)
    {
        var response = await client.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<List<string>> HistoryAsync(HttpClient client, Guid chat) =>
        [.. (await GetJsonAsync(client, $"/api/chats/{chat}/messages/cursor"))
            .GetProperty("items").EnumerateArray().Select(m => m.GetProperty("text").GetString()!)];

    private static async Task<JsonElement> ChatItemAsync(HttpClient client, Guid chat) =>
        await GetJsonAsync(client, $"/api/chats/{chat}/item");

    private static async Task<List<JsonElement>> JournalAsync(HttpClient client, long since = 0) =>
        [.. (await GetJsonAsync(client, $"/api/sync?since={since}")).GetProperty("updates").EnumerateArray()];

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await ReadJsonAsync(response)).GetProperty("errorCode").GetString();

    [Fact]
    public async Task Edit_ChangesTheText_ForEveryone_AndReachesTheOtherMemberLiveAndThroughSync()
    {
        await using var t = await ArrangeAsync();
        var message = await SendAsync(t.AliceApi, t.Chat, "helo");

        var updated = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var bobHub = t.Factory.CreateHubConnection(t.Bob.Token);
        bobHub.On<JsonElement>("MessageUpdated", m => updated.TrySetResult(m));
        await bobHub.StartAsync();

        var response = await t.AliceApi.PatchAsJsonAsync($"/api/chats/{t.Chat}/messages/{message}", new { text = "hello" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("hello", body.GetProperty("text").GetString());
        Assert.Equal(JsonValueKind.String, body.GetProperty("editedAt").ValueKind);
        Assert.Equal("text", body.GetProperty("type").GetString());

        var live = await updated.Task.WaitAsync(Wait);
        Assert.Equal(message, live.GetProperty("id").GetGuid());
        Assert.Equal("hello", live.GetProperty("text").GetString());

        Assert.Equal(["hello"], await HistoryAsync(t.BobApi, t.Chat));
        var journal = await JournalAsync(t.BobApi);
        var entry = Assert.Single(journal, u => u.GetProperty("type").GetString() == "MessageUpdated");
        Assert.Equal("hello", entry.GetProperty("payload").GetProperty("text").GetString());
        Assert.Equal("hello", (await ChatItemAsync(t.BobApi, t.Chat)).GetProperty("lastMessage").GetProperty("text").GetString());
    }

    [Fact]
    public async Task Edit_Errors_HaveCodes()
    {
        await using var t = await ArrangeAsync();
        var mine = await SendAsync(t.AliceApi, t.Chat, "mine");
        var old = await Data.MessageAsync(t.Chat, t.Alice.UserId, "long ago", TestData.T0);
        var carol = await t.Factory.RegisterAsync("carol");
        using var carolApi = t.Factory.CreateClient(carol.Token);

        var notAuthor = await t.BobApi.PatchAsJsonAsync($"/api/chats/{t.Chat}/messages/{mine}", new { text = "bob's now" });
        Assert.Equal(HttpStatusCode.Forbidden, notAuthor.StatusCode);
        Assert.Equal("NOT_MESSAGE_AUTHOR", await ErrorCodeAsync(notAuthor));

        var expired = await t.AliceApi.PatchAsJsonAsync($"/api/chats/{t.Chat}/messages/{old}", new { text = "fix" });
        Assert.Equal(HttpStatusCode.Forbidden, expired.StatusCode);
        Assert.Equal("EDIT_WINDOW_EXPIRED", await ErrorCodeAsync(expired));

        var empty = await t.AliceApi.PatchAsJsonAsync($"/api/chats/{t.Chat}/messages/{mine}", new { text = " " });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal("MESSAGE_EMPTY", await ErrorCodeAsync(empty));

        var outsider = await carolApi.PatchAsJsonAsync($"/api/chats/{t.Chat}/messages/{mine}", new { text = "hi" });
        Assert.Equal(HttpStatusCode.Forbidden, outsider.StatusCode);
        Assert.Equal("NOT_A_MEMBER", await ErrorCodeAsync(outsider));

        var unknown = await t.AliceApi.PatchAsJsonAsync($"/api/chats/{t.Chat}/messages/{Guid.NewGuid()}", new { text = "hi" });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("MESSAGE_NOT_FOUND", await ErrorCodeAsync(unknown));
    }

    [Fact]
    public async Task DeleteForEveryone_RemovesTheMessageFromHistorySearchListAndUnread_ForBothMembers()
    {
        await using var t = await ArrangeAsync();
        await SendAsync(t.AliceApi, t.Chat, "first message");
        var oops = await SendAsync(t.AliceApi, t.Chat, "oops wrong chat");
        Assert.Equal(2, (await ChatItemAsync(t.BobApi, t.Chat)).GetProperty("unreadCount").GetInt32());

        var deleted = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var bobHub = t.Factory.CreateHubConnection(t.Bob.Token);
        bobHub.On<JsonElement>("MessageDeleted", d => deleted.TrySetResult(d));
        await bobHub.StartAsync();

        var response = await t.AliceApi.DeleteAsync($"/api/chats/{t.Chat}/messages/{oops}?forEveryone=true");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var live = await deleted.Task.WaitAsync(Wait);
        Assert.Equal(oops, live.GetProperty("messageId").GetGuid());
        Assert.Equal(t.Chat, live.GetProperty("chatId").GetGuid());
        Assert.True(live.GetProperty("forEveryone").GetBoolean());

        foreach (var api in new[] { t.AliceApi, t.BobApi })
        {
            Assert.Equal(["first message"], await HistoryAsync(api, t.Chat));
            var search = await GetJsonAsync(api, $"/api/chats/{t.Chat}/messages/search?q=oops");
            Assert.Equal(0, search.GetProperty("totalCount").GetInt32());
            Assert.Equal("first message",
                (await ChatItemAsync(api, t.Chat)).GetProperty("lastMessage").GetProperty("text").GetString());
        }
        Assert.Equal(1, (await ChatItemAsync(t.BobApi, t.Chat)).GetProperty("unreadCount").GetInt32());
        Assert.Contains(await JournalAsync(t.BobApi), u => u.GetProperty("type").GetString() == "MessageDeleted");

        // A repeat is quiet: 204, and no second journal entry.
        Assert.Equal(HttpStatusCode.NoContent,
            (await t.AliceApi.DeleteAsync($"/api/chats/{t.Chat}/messages/{oops}?forEveryone=true")).StatusCode);
        Assert.Single(await JournalAsync(t.BobApi), u => u.GetProperty("type").GetString() == "MessageDeleted");

        // A deleted message cannot be edited.
        var edit = await t.AliceApi.PatchAsJsonAsync($"/api/chats/{t.Chat}/messages/{oops}", new { text = "back" });
        Assert.Equal(HttpStatusCode.NotFound, edit.StatusCode);
    }

    [Fact]
    public async Task DeleteForEveryone_SomeoneElsesMessage_IsForbidden()
    {
        await using var t = await ArrangeAsync();
        var message = await SendAsync(t.AliceApi, t.Chat, "alice's");

        var response = await t.BobApi.DeleteAsync($"/api/chats/{t.Chat}/messages/{message}?forEveryone=true");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("NOT_MESSAGE_AUTHOR", await ErrorCodeAsync(response));
        Assert.Equal(["alice's"], await HistoryAsync(t.BobApi, t.Chat));
    }

    [Fact]
    public async Task DeleteForMe_HidesTheMessageOnlyForTheCaller_AndOnlyTheirJournalHearsOfIt()
    {
        await using var t = await ArrangeAsync();
        await SendAsync(t.AliceApi, t.Chat, "keep");
        var rude = await SendAsync(t.AliceApi, t.Chat, "rude remark");
        var bobPtsBefore = (await GetJsonAsync(t.BobApi, "/api/sync/state")).GetProperty("pts").GetInt64();
        var alicePtsBefore = (await GetJsonAsync(t.AliceApi, "/api/sync/state")).GetProperty("pts").GetInt64();

        var response = await t.BobApi.DeleteAsync($"/api/chats/{t.Chat}/messages/{rude}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["keep"], await HistoryAsync(t.BobApi, t.Chat));
        Assert.Equal(["keep", "rude remark"], await HistoryAsync(t.AliceApi, t.Chat));

        var bobItem = await ChatItemAsync(t.BobApi, t.Chat);
        Assert.Equal("keep", bobItem.GetProperty("lastMessage").GetProperty("text").GetString());
        Assert.Equal(1, bobItem.GetProperty("unreadCount").GetInt32());
        Assert.Equal(0, (await GetJsonAsync(t.BobApi, $"/api/chats/{t.Chat}/messages/search?q=rude")).GetProperty("totalCount").GetInt32());

        var bobUpdate = Assert.Single(await JournalAsync(t.BobApi, bobPtsBefore));
        Assert.Equal("MessageDeleted", bobUpdate.GetProperty("type").GetString());
        Assert.False(bobUpdate.GetProperty("payload").GetProperty("forEveryone").GetBoolean());
        Assert.Empty(await JournalAsync(t.AliceApi, alicePtsBefore));
    }

    [Fact]
    public async Task Delete_UnknownMessage_Is404_AndOutsider_Is403()
    {
        await using var t = await ArrangeAsync();
        var message = await SendAsync(t.AliceApi, t.Chat, "hi");
        var carol = await t.Factory.RegisterAsync("carol");
        using var carolApi = t.Factory.CreateClient(carol.Token);

        var unknown = await t.AliceApi.DeleteAsync($"/api/chats/{t.Chat}/messages/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("MESSAGE_NOT_FOUND", await ErrorCodeAsync(unknown));

        var outsider = await carolApi.DeleteAsync($"/api/chats/{t.Chat}/messages/{message}");
        Assert.Equal(HttpStatusCode.Forbidden, outsider.StatusCode);
        Assert.Equal("NOT_A_MEMBER", await ErrorCodeAsync(outsider));
    }
}
