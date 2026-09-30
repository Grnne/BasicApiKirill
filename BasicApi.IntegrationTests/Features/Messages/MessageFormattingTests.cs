using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;

namespace BasicApi.IntegrationTests.Features.Messages;

/// <summary>Formatting and mentions over REST (plan 2, F1.3).</summary>
public class MessageFormattingTests(PostgresFixture db) : DbTest(db)
{
    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private static async Task<int> MentionsAsync(HttpClient client, Guid chat) =>
        (await GetJsonAsync(client, $"/api/chats/{chat}/item")).GetProperty("unreadMentionCount").GetInt32();

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await ReadJsonAsync(response)).GetProperty("errorCode").GetString();

    private sealed record Arranged(ApiFactory Factory, AuthResult Alice, AuthResult Bob, AuthResult Carol, Guid Group) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Factory.DisposeAsync();
    }

    private async Task<Arranged> ArrangeAsync()
    {
        var factory = new ApiFactory(Db.ConnectionString,
            new Dictionary<string, string?> { ["RateLimiting:CommandsPer10Seconds"] = "1000" });
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carol = await factory.RegisterAsync("carol");
        var group = await Data.GroupChatAsync("team", [alice.UserId, bob.UserId, carol.UserId]);
        return new Arranged(factory, alice, bob, carol, group);
    }

    [Fact]
    public async Task Formatting_IsStoredAgainstTheTrimmedText_AndComesBackInHistory()
    {
        await using var t = await ArrangeAsync();
        using var aliceApi = t.Factory.CreateClient(t.Alice.Token);

        var response = await aliceApi.PostAsJsonAsync($"/api/chats/{t.Group}/messages", new
        {
            text = "  read the docs  ",
            entities = new object[]
            {
                new { type = "bold", offset = 2, length = 4 },
                new { type = "link", offset = 11, length = 4, url = "https://example.com/docs" }
            }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var sent = await ReadJsonAsync(response);
        Assert.Equal("read the docs", sent.GetProperty("text").GetString());

        var history = await GetJsonAsync(aliceApi, $"/api/chats/{t.Group}/messages/cursor");
        var entities = history.GetProperty("items")[0].GetProperty("entities").EnumerateArray().ToList();
        Assert.Equal(2, entities.Count);
        Assert.Equal(("bold", 0, 4), (entities[0].GetProperty("type").GetString(), entities[0].GetProperty("offset").GetInt32(), entities[0].GetProperty("length").GetInt32()));
        Assert.Equal(("link", 9, 4), (entities[1].GetProperty("type").GetString(), entities[1].GetProperty("offset").GetInt32(), entities[1].GetProperty("length").GetInt32()));
        Assert.Equal("https://example.com/docs", entities[1].GetProperty("url").GetString());
        Assert.False(entities[0].TryGetProperty("url", out _));
    }

    [Fact]
    public async Task Mention_CountsForTheMentionedMemberUntilRead_AndFollowsEditsAndDeletes()
    {
        await using var t = await ArrangeAsync();
        using var aliceApi = t.Factory.CreateClient(t.Alice.Token);
        using var bobApi = t.Factory.CreateClient(t.Bob.Token);
        using var carolApi = t.Factory.CreateClient(t.Carol.Token);

        var mention = new { type = "mention", offset = 0, length = 4, userId = t.Bob.UserId };
        var first = await ReadJsonAsync(await aliceApi.PostAsJsonAsync($"/api/chats/{t.Group}/messages",
            new { text = "@bob look", entities = new[] { mention } }));
        var second = await ReadJsonAsync(await aliceApi.PostAsJsonAsync($"/api/chats/{t.Group}/messages",
            new { text = "@bob and this", entities = new[] { mention } }));

        Assert.Equal(2, await MentionsAsync(bobApi, t.Group));
        Assert.Equal(0, await MentionsAsync(carolApi, t.Group));
        Assert.Equal(0, await MentionsAsync(aliceApi, t.Group));

        // An edit that drops the mention takes it off the counter.
        var edit = await aliceApi.PatchAsJsonAsync($"/api/chats/{t.Group}/messages/{second.GetProperty("id").GetGuid()}",
            new { text = "@bob and this" });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Empty((await ReadJsonAsync(edit)).GetProperty("entities").EnumerateArray());
        Assert.Equal(1, await MentionsAsync(bobApi, t.Group));

        // An edit that adds one to Carol puts it on hers.
        await aliceApi.PatchAsJsonAsync($"/api/chats/{t.Group}/messages/{second.GetProperty("id").GetGuid()}",
            new { text = "@carol and this", entities = new[] { new { type = "mention", offset = 0, length = 6, userId = t.Carol.UserId } } });
        Assert.Equal(1, await MentionsAsync(carolApi, t.Group));

        // Deleting the message takes it off; reading takes off the rest.
        (await aliceApi.DeleteAsync($"/api/chats/{t.Group}/messages/{second.GetProperty("id").GetGuid()}?forEveryone=true")).EnsureSuccessStatusCode();
        Assert.Equal(0, await MentionsAsync(carolApi, t.Group));

        (await bobApi.PostAsJsonAsync($"/api/chats/{t.Group}/read", new { lastMessageId = first.GetProperty("id").GetGuid() })).EnsureSuccessStatusCode();
        Assert.Equal(0, await MentionsAsync(bobApi, t.Group));
    }

    [Fact]
    public async Task Mention_OfANonMember_AndUnsafeLinks_AreRejected()
    {
        await using var t = await ArrangeAsync();
        var dave = await t.Factory.RegisterAsync("dave");
        using var aliceApi = t.Factory.CreateClient(t.Alice.Token);

        var outsider = await aliceApi.PostAsJsonAsync($"/api/chats/{t.Group}/messages", new
        {
            text = "@dave",
            entities = new[] { new { type = "mention", offset = 0, length = 5, userId = dave.UserId } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, outsider.StatusCode);
        Assert.Equal("INVALID_ENTITIES", await ErrorCodeAsync(outsider));

        var script = await aliceApi.PostAsJsonAsync($"/api/chats/{t.Group}/messages", new
        {
            text = "click",
            entities = new[] { new { type = "link", offset = 0, length = 5, url = "javascript:alert(1)" } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, script.StatusCode);
        Assert.Equal("INVALID_ENTITIES", await ErrorCodeAsync(script));

        var beyond = await aliceApi.PostAsJsonAsync($"/api/chats/{t.Group}/messages", new
        {
            text = "short",
            entities = new[] { new { type = "bold", offset = 3, length = 10 } }
        });
        Assert.Equal("INVALID_ENTITIES", await ErrorCodeAsync(beyond));

        Assert.Empty((await GetJsonAsync(aliceApi, $"/api/chats/{t.Group}/messages/cursor")).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Forward_KeepsTheFormatting_ButNotifiesNobody()
    {
        await using var t = await ArrangeAsync();
        using var aliceApi = t.Factory.CreateClient(t.Alice.Token);
        using var bobApi = t.Factory.CreateClient(t.Bob.Token);
        var aliceBob = await Data.PrivateChatAsync(t.Alice.UserId, t.Bob.UserId);

        var original = await ReadJsonAsync(await aliceApi.PostAsJsonAsync($"/api/chats/{aliceBob}/messages", new
        {
            text = "@bob ping",
            entities = new[] { new { type = "mention", offset = 0, length = 4, userId = t.Bob.UserId } }
        }));
        (await bobApi.PostAsJsonAsync($"/api/chats/{aliceBob}/read", new { lastMessageId = original.GetProperty("id").GetGuid() })).EnsureSuccessStatusCode();

        var forward = await aliceApi.PostAsJsonAsync($"/api/chats/{t.Group}/messages/forward",
            new { fromChatId = aliceBob, messageIds = new[] { original.GetProperty("id").GetGuid() } });

        Assert.Equal(HttpStatusCode.Created, forward.StatusCode);
        var copy = (await ReadJsonAsync(forward)).GetProperty("items")[0];
        Assert.Equal("mention", copy.GetProperty("entities")[0].GetProperty("type").GetString());
        Assert.Equal(0, await MentionsAsync(bobApi, t.Group));
    }
}
