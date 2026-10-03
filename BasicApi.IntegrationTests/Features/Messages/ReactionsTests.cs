using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;
using BasicApi.Storage.Repositories;
using Microsoft.AspNetCore.SignalR.Client;

namespace BasicApi.IntegrationTests.Features.Messages;

/// <summary>Reactions over REST, with events, sync and concurrency.</summary>
public class ReactionsTests(PostgresFixture db) : DbTest(db)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<JsonElement> LastMessageAsync(HttpClient client, Guid chat)
    {
        var response = await client.GetAsync($"/api/chats/{chat}/messages/cursor");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("items").EnumerateArray().Last();
    }

    private static List<(string Emoji, int Count)> Counts(JsonElement reactions) =>
        [.. reactions.EnumerateArray().Select(r => (r.GetProperty("emoji").GetString()!, r.GetProperty("count").GetInt32()))];

    private static Task<HttpResponseMessage> ReactAsync(HttpClient client, Guid chat, Guid message, string emoji) =>
        client.PutAsJsonAsync($"/api/chats/{chat}/messages/{message}/reactions", new { emoji });

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await ReadJsonAsync(response)).GetProperty("errorCode").GetString();

    private static async Task<List<JsonElement>> JournalAsync(HttpClient client, string type)
    {
        var response = await client.GetAsync("/api/sync?since=0");
        return [.. (await ReadJsonAsync(response)).GetProperty("updates").EnumerateArray()
            .Where(u => u.GetProperty("type").GetString() == type)];
    }

    [Fact]
    public async Task Reactions_AreCounted_ReplacedAndRemoved_AndEveryoneSeesThemLiveAndInHistory()
    {
        await using var factory = new ApiFactory(Db.ConnectionString,
            new Dictionary<string, string?> { ["RateLimiting:CommandsPer10Seconds"] = "1000" });
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carol = await factory.RegisterAsync("carol");
        var chat = await Data.GroupChatAsync("team", [alice.UserId, bob.UserId, carol.UserId]);
        var message = await Data.MessageAsync(chat, alice.UserId, "we shipped it", TestData.T0);
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        using var carolApi = factory.CreateClient(carol.Token);

        var changes = new List<JsonElement>();
        var gotThree = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var aliceHub = factory.CreateHubConnection(alice.Token);
        aliceHub.On<JsonElement>("ReactionsChanged", r =>
        {
            lock (changes)
            {
                changes.Add(r);
                if (changes.Count == 3) gotThree.TrySetResult();
            }
        });
        await aliceHub.StartAsync();

        var bobResponse = await ReactAsync(bobApi, chat, message, "🔥");
        Assert.Equal(HttpStatusCode.OK, bobResponse.StatusCode);
        Assert.Equal([("🔥", 1)], Counts((await ReadJsonAsync(bobResponse)).GetProperty("reactions")));

        (await ReactAsync(carolApi, chat, message, "🔥")).EnsureSuccessStatusCode();
        // Bob changes his mind: one reaction per user, the new one replaces the old.
        (await ReactAsync(bobApi, chat, message, "🎉")).EnsureSuccessStatusCode();
        // The same again changes nothing.
        (await ReactAsync(bobApi, chat, message, "🎉")).EnsureSuccessStatusCode();

        await gotThree.Task.WaitAsync(Wait);
        var last = changes[^1];
        Assert.Equal(bob.UserId, last.GetProperty("userId").GetGuid());
        Assert.Equal("🎉", last.GetProperty("emoji").GetString());
        Assert.Equal([("🔥", 1), ("🎉", 1)], Counts(last.GetProperty("reactions")));

        var seenByBob = await LastMessageAsync(bobApi, chat);
        Assert.Equal([("🔥", 1), ("🎉", 1)], Counts(seenByBob.GetProperty("reactions")));
        Assert.Equal("🎉", seenByBob.GetProperty("myReaction").GetString());
        Assert.Equal("🔥", (await LastMessageAsync(carolApi, chat)).GetProperty("myReaction").GetString());
        Assert.Equal(JsonValueKind.Null, (await LastMessageAsync(aliceApi, chat)).GetProperty("myReaction").ValueKind);
        Assert.Equal(3, (await JournalAsync(aliceApi, "ReactionsChanged")).Count);

        // Removing: the event carries emoji null; removing again is quiet.
        Assert.Equal(HttpStatusCode.NoContent,
            (await carolApi.DeleteAsync($"/api/chats/{chat}/messages/{message}/reactions")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await carolApi.DeleteAsync($"/api/chats/{chat}/messages/{message}/reactions")).StatusCode);
        var journal = await JournalAsync(aliceApi, "ReactionsChanged");
        Assert.Equal(4, journal.Count);
        Assert.Equal(JsonValueKind.Null, journal[^1].GetProperty("payload").GetProperty("emoji").ValueKind);
        Assert.Equal([("🎉", 1)], Counts((await LastMessageAsync(aliceApi, chat)).GetProperty("reactions")));
    }

    [Fact]
    public async Task Reactions_Errors_HaveCodes_AndATombstoneLosesItsReactions()
    {
        await using var factory = new ApiFactory(Db.ConnectionString,
            new Dictionary<string, string?> { ["RateLimiting:CommandsPer10Seconds"] = "1000" });
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carol = await factory.RegisterAsync("carol");
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        using var carolApi = factory.CreateClient(carol.Token);
        var sent = await ReadJsonAsync(await aliceApi.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text = "hi" }));
        var message = sent.GetProperty("id").GetGuid();

        var unicorn = await ReactAsync(bobApi, chat, message, "🦄");
        Assert.Equal(HttpStatusCode.BadRequest, unicorn.StatusCode);
        Assert.Equal("INVALID_REACTION", await ErrorCodeAsync(unicorn));

        var outsider = await ReactAsync(carolApi, chat, message, "👍");
        Assert.Equal(HttpStatusCode.Forbidden, outsider.StatusCode);
        Assert.Equal("NOT_A_MEMBER", await ErrorCodeAsync(outsider));

        var unknown = await ReactAsync(bobApi, chat, Guid.NewGuid(), "👍");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("MESSAGE_NOT_FOUND", await ErrorCodeAsync(unknown));

        (await ReactAsync(bobApi, chat, message, "👍")).EnsureSuccessStatusCode();
        (await aliceApi.DeleteAsync($"/api/chats/{chat}/messages/{message}?forEveryone=true")).EnsureSuccessStatusCode();

        var onTombstone = await ReactAsync(bobApi, chat, message, "❤️");
        Assert.Equal(HttpStatusCode.NotFound, onTombstone.StatusCode);
    }

    [Fact]
    public async Task ConcurrentReactions_AreAllCountedInTheSummary()
    {
        // Twenty members react to one message at once, each in its own transaction.
        // Without the row lock a summary computed next to a concurrent insert would miss it.
        var author = await Data.UserAsync("author");
        var members = new List<Guid> { author };
        for (var i = 0; i < 20; i++)
            members.Add(await Data.UserAsync($"fan{i}"));
        var chat = await Data.GroupChatAsync("fans", [.. members]);
        var message = await Data.MessageAsync(chat, author, "concert tonight", TestData.T0);

        await Task.WhenAll(members.Skip(1).Select((user, i) => Task.Run(async () =>
        {
            await using var session = NewSession();
            await new ReactionRepository(session).SetAsync(chat, message, user, i % 2 == 0 ? "🔥" : "🎉");
        })));

        await using var check = NewSession();
        var page = await new MessageRepository(check).GetMessagesWithSenderCursorAsync(chat, author, null, 10);
        var summary = JsonDocument.Parse(page.Items[0].ReactionsJson!).RootElement;
        Assert.Equal(20, summary.EnumerateArray().Sum(r => r.GetProperty("count").GetInt32()));
        Assert.Equal([10, 10], summary.EnumerateArray().Select(r => r.GetProperty("count").GetInt32()));
    }

    private static async Task<int> UnreadReactionsAsync(HttpClient client, Guid chat) =>
        (await client.ChatItemAsync(chat)).GetProperty("unreadReactionCount").GetInt32();

    [Fact]
    public async Task OthersReactions_ToYourMessages_AreNewUntilYouReadTheChat()
    {
        await using var factory = new ApiFactory(Db.ConnectionString,
            new Dictionary<string, string?> { ["RateLimiting:CommandsPer10Seconds"] = "1000" });
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carol = await factory.RegisterAsync("carol");
        var chat = await Data.GroupChatAsync("team", [alice.UserId, bob.UserId, carol.UserId]);
        var mine = await Data.MessageAsync(chat, alice.UserId, "we shipped it", TestData.T0);
        var bobs = await Data.MessageAsync(chat, bob.UserId, "great", TestData.T0.AddMinutes(1));
        // Recent: deleting for everyone has a time window.
        var gone = await Data.MessageAsync(chat, alice.UserId, "oops", DateTime.UtcNow);
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        using var carolApi = factory.CreateClient(carol.Token);
        (await aliceApi.PostAsJsonAsync($"/api/chats/{chat}/read", new { lastMessageId = bobs })).EnsureSuccessStatusCode();

        (await ReactAsync(bobApi, chat, mine, "🔥")).EnsureSuccessStatusCode();
        (await ReactAsync(carolApi, chat, mine, "🔥")).EnsureSuccessStatusCode();
        (await ReactAsync(carolApi, chat, gone, "👍")).EnsureSuccessStatusCode();
        // One's own reaction is no news; a message deleted for everyone takes its reactions with it.
        (await ReactAsync(aliceApi, chat, mine, "🎉")).EnsureSuccessStatusCode();
        (await aliceApi.DeleteAsync($"/api/chats/{chat}/messages/{gone}?forEveryone=true")).EnsureSuccessStatusCode();

        Assert.Equal(2, await UnreadReactionsAsync(aliceApi, chat));
        Assert.Equal(0, await UnreadReactionsAsync(bobApi, chat));
        Assert.Contains((await aliceApi.GetJsonAsync("/api/chats")).EnumerateArray(),
            c => c.Id("chatId") == chat && c.GetProperty("unreadReactionCount").GetInt32() == 2);
        // Alice's devices keep the count from the journal, without reloading the list.
        Assert.Equal([0, 1, 2, 3], (await aliceApi.JournalAsync("ReadStateChanged"))
            .Select(s => s.GetProperty("unreadReactionCount").GetInt32()));

        // Taking a reaction back takes it from the count.
        Assert.Equal(HttpStatusCode.NoContent,
            (await carolApi.DeleteAsync($"/api/chats/{chat}/messages/{mine}/reactions")).StatusCode);
        Assert.Equal(1, await UnreadReactionsAsync(aliceApi, chat));

        // Reading the chat sees them, even with no new messages to read: the pointer is already there.
        (await aliceApi.PostAsJsonAsync($"/api/chats/{chat}/read", new { lastMessageId = bobs })).EnsureSuccessStatusCode();
        Assert.Equal(0, await UnreadReactionsAsync(aliceApi, chat));
        Assert.Equal(0, (await aliceApi.JournalAsync("ReadStateChanged"))[^1].GetProperty("unreadReactionCount").GetInt32());

        // A reaction after that is new again; a changed one is new too.
        (await ReactAsync(bobApi, chat, mine, "🎉")).EnsureSuccessStatusCode();
        Assert.Equal(1, await UnreadReactionsAsync(aliceApi, chat));
    }
}
