using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;

namespace BasicApi.IntegrationTests.Features.Messages;

/// <summary>Replies and forwards over REST.</summary>
public class MessageReplyForwardTests(PostgresFixture db) : DbTest(db)
{
    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<JsonElement> SendAsync(HttpClient client, Guid chat, object body)
    {
        var response = await client.PostAsJsonAsync($"/api/chats/{chat}/messages", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private static async Task<List<JsonElement>> HistoryAsync(HttpClient client, Guid chat)
    {
        var response = await client.GetAsync($"/api/chats/{chat}/messages/cursor");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. (await ReadJsonAsync(response)).GetProperty("items").EnumerateArray()];
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await ReadJsonAsync(response)).GetProperty("errorCode").GetString();

    private static ApiFactory NewFactory(string connectionString) =>
        new(connectionString, new Dictionary<string, string?> { ["RateLimiting:CommandsPer10Seconds"] = "1000" });

    [Fact]
    public async Task Reply_CarriesAPreviewOfTheAnsweredMessage_WhichTurnsIntoATombstoneWhenItIsDeleted()
    {
        await using var factory = NewFactory(Db.ConnectionString);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);

        var question = (await SendAsync(aliceApi, chat, new { text = "Lunch at noon?" })).GetProperty("id").GetGuid();
        var answer = await SendAsync(bobApi, chat, new { text = "Sure", replyToMessageId = question });

        var replyTo = answer.GetProperty("replyTo");
        Assert.Equal(question, replyTo.GetProperty("messageId").GetGuid());
        Assert.Equal(alice.UserId, replyTo.GetProperty("senderId").GetGuid());
        Assert.Equal("alice", replyTo.GetProperty("senderName").GetString());
        Assert.Equal("Lunch at noon?", replyTo.GetProperty("text").GetString());
        Assert.False(replyTo.GetProperty("deleted").GetBoolean());
        Assert.Equal(JsonValueKind.Null, answer.GetProperty("forwardFrom").ValueKind);

        (await aliceApi.DeleteAsync($"/api/chats/{chat}/messages/{question}?forEveryone=true")).EnsureSuccessStatusCode();

        var stored = Assert.Single(await HistoryAsync(aliceApi, chat));
        Assert.Equal("Sure", stored.GetProperty("text").GetString());
        Assert.True(stored.GetProperty("replyTo").GetProperty("deleted").GetBoolean());
        Assert.Equal("", stored.GetProperty("replyTo").GetProperty("text").GetString());
    }

    [Fact]
    public async Task Reply_ToAMessageOfAnotherChat_IsRejected()
    {
        await using var factory = NewFactory(Db.ConnectionString);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carol = await factory.RegisterAsync("carol");
        var withBob = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        var withCarol = await Data.PrivateChatAsync(alice.UserId, carol.UserId);
        var secret = await Data.MessageAsync(withBob, bob.UserId, "just between us", TestData.T0);
        using var aliceApi = factory.CreateClient(alice.Token);

        var response = await aliceApi.PostAsJsonAsync($"/api/chats/{withCarol}/messages",
            new { text = "look", replyToMessageId = secret });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("REPLY_TARGET_NOT_FOUND", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Forward_CopiesMessagesToAnotherChat_WithTheOriginalAuthor_AndARetryCreatesNothingNew()
    {
        await using var factory = NewFactory(Db.ConnectionString);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carol = await factory.RegisterAsync("carol");
        var withBob = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        var withCarol = await Data.PrivateChatAsync(alice.UserId, carol.UserId);
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        using var carolApi = factory.CreateClient(carol.Token);

        var first = (await SendAsync(bobApi, withBob, new { text = "address: Main st 1" })).GetProperty("id").GetGuid();
        var second = (await SendAsync(bobApi, withBob, new { text = "door code 42" })).GetProperty("id").GetGuid();
        var clientIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var request = new { fromChatId = withBob, messageIds = new[] { second, first }, clientMessageIds = clientIds };

        var response = await aliceApi.PostAsJsonAsync($"/api/chats/{withCarol}/messages/forward", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var items = (await ReadJsonAsync(response)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["address: Main st 1", "door code 42"], items.Select(m => m.GetProperty("text").GetString()));

        var seen = await HistoryAsync(carolApi, withCarol);
        Assert.Equal(["address: Main st 1", "door code 42"], seen.Select(m => m.GetProperty("text").GetString()));
        Assert.All(seen, m =>
        {
            Assert.Equal(alice.UserId, m.GetProperty("senderId").GetGuid());
            Assert.Equal(bob.UserId, m.GetProperty("forwardFrom").GetProperty("senderId").GetGuid());
            Assert.Equal("bob", m.GetProperty("forwardFrom").GetProperty("senderName").GetString());
        });

        var retry = await aliceApi.PostAsJsonAsync($"/api/chats/{withCarol}/messages/forward", request);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(items.Select(m => m.GetProperty("id").GetGuid()),
            (await ReadJsonAsync(retry)).GetProperty("items").EnumerateArray().Select(m => m.GetProperty("id").GetGuid()));
        Assert.Equal(2, (await HistoryAsync(carolApi, withCarol)).Count);

        // Carol passes it on — the author is still Bob; and Alice cannot edit Bob's words.
        var withAlice = await Data.GroupChatAsync("club", [carol.UserId, alice.UserId]);
        var chain = await carolApi.PostAsJsonAsync($"/api/chats/{withAlice}/messages/forward",
            new { fromChatId = withCarol, messageIds = new[] { items[0].GetProperty("id").GetGuid() } });
        Assert.Equal(HttpStatusCode.Created, chain.StatusCode);
        Assert.Equal(bob.UserId, (await ReadJsonAsync(chain)).GetProperty("items")[0]
            .GetProperty("forwardFrom").GetProperty("senderId").GetGuid());

        var edit = await aliceApi.PatchAsJsonAsync(
            $"/api/chats/{withCarol}/messages/{items[0].GetProperty("id").GetGuid()}", new { text = "fake address" });
        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
        Assert.Equal("MESSAGE_NOT_EDITABLE", await ErrorCodeAsync(edit));
    }

    [Fact]
    public async Task Forward_FromAChatTheUserIsNotIn_OrOfAHiddenMessage_IsRefused()
    {
        await using var factory = NewFactory(Db.ConnectionString);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carol = await factory.RegisterAsync("carol");
        var aliceBob = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        var bobCarol = await Data.PrivateChatAsync(bob.UserId, carol.UserId);
        var foreign = await Data.MessageAsync(bobCarol, carol.UserId, "not for alice", TestData.T0);
        var hidden = await Data.MessageAsync(aliceBob, bob.UserId, "alice hid this", TestData.T0);
        using var aliceApi = factory.CreateClient(alice.Token);
        (await aliceApi.DeleteAsync($"/api/chats/{aliceBob}/messages/{hidden}")).EnsureSuccessStatusCode();

        var notMember = await aliceApi.PostAsJsonAsync($"/api/chats/{aliceBob}/messages/forward",
            new { fromChatId = bobCarol, messageIds = new[] { foreign } });
        Assert.Equal(HttpStatusCode.Forbidden, notMember.StatusCode);
        Assert.Equal("NOT_A_MEMBER", await ErrorCodeAsync(notMember));

        var wrongChat = await aliceApi.PostAsJsonAsync($"/api/chats/{aliceBob}/messages/forward",
            new { fromChatId = aliceBob, messageIds = new[] { foreign } });
        Assert.Equal(HttpStatusCode.NotFound, wrongChat.StatusCode);
        Assert.Equal("MESSAGE_NOT_FOUND", await ErrorCodeAsync(wrongChat));

        var hiddenOne = await aliceApi.PostAsJsonAsync($"/api/chats/{aliceBob}/messages/forward",
            new { fromChatId = aliceBob, messageIds = new[] { hidden } });
        Assert.Equal(HttpStatusCode.NotFound, hiddenOne.StatusCode);
    }
}
