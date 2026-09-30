using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;
using BasicApi.Storage.Repositories;

namespace BasicApi.IntegrationTests.Features.Chats;

/// <summary>"Saved Messages" — a chat with oneself.</summary>
public class SavedMessagesTests(PostgresFixture db) : DbTest(db)
{
    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task SavedMessages_IsCreatedOnFirstOpen_ThenTheSameChat_AndWorksLikeAnyChat()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);

        var first = await aliceApi.PostAsync("/api/chats/saved", null);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var chat = await ReadJsonAsync(first);
        Assert.Equal("saved", chat.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, chat.GetProperty("title").ValueKind);
        Assert.Equal(JsonValueKind.Null, chat.GetProperty("companionId").ValueKind);
        var chatId = chat.GetProperty("chatId").GetGuid();

        var again = await aliceApi.PostAsync("/api/chats/saved", null);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(chatId, (await ReadJsonAsync(again)).GetProperty("chatId").GetGuid());

        // Bob has his own.
        var bobs = await ReadJsonAsync(await bobApi.PostAsync("/api/chats/saved", null));
        Assert.NotEqual(chatId, bobs.GetProperty("chatId").GetGuid());

        // A note to self, and a forward from a chat with Bob.
        (await aliceApi.PostAsJsonAsync($"/api/chats/{chatId}/messages", new { text = "buy milk" })).EnsureSuccessStatusCode();
        var withBob = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        var tip = await Data.MessageAsync(withBob, bob.UserId, "the wifi password is on the fridge", TestData.T0);
        var forward = await aliceApi.PostAsJsonAsync($"/api/chats/{chatId}/messages/forward",
            new { fromChatId = withBob, messageIds = new[] { tip } });
        Assert.Equal(HttpStatusCode.Created, forward.StatusCode);

        var item = await ReadJsonAsync(await aliceApi.GetAsync($"/api/chats/{chatId}/item"));
        Assert.Equal(0, item.GetProperty("unreadCount").GetInt32());
        Assert.Equal("the wifi password is on the fridge", item.GetProperty("lastMessage").GetProperty("text").GetString());

        var list = await ReadJsonAsync(await aliceApi.GetAsync("/api/chats"));
        Assert.Contains(list.EnumerateArray(), c => c.GetProperty("chatId").GetGuid() == chatId);

        // Nobody else can get in.
        var peek = await bobApi.GetAsync($"/api/chats/{chatId}/messages/cursor");
        Assert.Equal(HttpStatusCode.Forbidden, peek.StatusCode);

        // The user's other devices hear of it through sync.
        var journal = await ReadJsonAsync(await aliceApi.GetAsync("/api/sync?since=0"));
        Assert.Contains(journal.GetProperty("updates").EnumerateArray(), u =>
            u.GetProperty("type").GetString() == "ChatCreated" &&
            u.GetProperty("payload").GetProperty("chatId").GetGuid() == chatId);
    }

    [Fact]
    public async Task ConcurrentFirstOpens_MakeOneChat()
    {
        var alice = await Data.UserAsync("alice");

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await using var session = NewSession();
            return await new ChatRepository(session).GetOrCreateSavedChatAsync(alice);
        })));

        Assert.Single(results.Select(r => r.ChatId).Distinct());
        Assert.Single(results, r => r.Created);
        await using var check = NewSession();
        Assert.Single(await new ChatRepository(check).GetUserChatsBatchedAsync(alice));
    }
}
