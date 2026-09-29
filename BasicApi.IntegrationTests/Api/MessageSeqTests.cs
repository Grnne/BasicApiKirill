using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;
using BasicApi.Storage.Dto;
using Dapper;
using Microsoft.AspNetCore.SignalR.Client;
using Npgsql;

namespace BasicApi.IntegrationTests.Api;

/// <summary>Номер сообщения в чате и идемпотентная отправка (план 1, 2.5).</summary>
public class MessageSeqTests(PostgresFixture db) : DbTest(db)
{
    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<(ApiFactory Factory, AuthResult Alice, AuthResult Bob, Guid Chat)> ArrangeAsync()
    {
        var factory = new ApiFactory(Db.ConnectionString,
            new Dictionary<string, string?> { ["RateLimiting:CommandsPer10Seconds"] = "1000" });
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        return (factory, alice, bob, chat);
    }

    private async Task<int> CountMessagesAsync(Guid chat)
    {
        await using var connection = new NpgsqlConnection(Db.ConnectionString);
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM messages WHERE chat_id = @chat", new { chat });
    }

    [Fact]
    public async Task Retry_WithTheSameClientMessageId_DoesNotCreateADuplicate()
    {
        var (factory, alice, bob, chat) = await ArrangeAsync();
        await using var _ = factory;
        using var client = factory.CreateClient(alice.Token);

        var events = 0;
        await using var bobHub = factory.CreateHubConnection(bob.Token);
        bobHub.On<Guid, JsonElement>("ChatListUpdated", (_, _) => Interlocked.Increment(ref events));
        await bobHub.StartAsync();

        var clientMessageId = Guid.NewGuid();
        var first = await client.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text = "once", clientMessageId });
        var retry = await client.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text = "once", clientMessageId });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var sent = await ReadJsonAsync(first);
        var repeated = await ReadJsonAsync(retry);
        Assert.Equal(sent.GetProperty("id").GetGuid(), repeated.GetProperty("id").GetGuid());
        Assert.Equal(sent.GetProperty("seq").GetInt64(), repeated.GetProperty("seq").GetInt64());
        Assert.Equal(clientMessageId, repeated.GetProperty("clientMessageId").GetGuid());
        Assert.Equal(1, await CountMessagesAsync(chat));

        // Событие — только от первой отправки. Отправим ещё одно, чтобы дождаться доставки.
        (await client.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text = "marker" })).EnsureSuccessStatusCode();
        await WaitUntilAsync(() => Volatile.Read(ref events) >= 2);
        await Task.Delay(200);
        Assert.Equal(2, Volatile.Read(ref events));
    }

    [Fact]
    public async Task ConcurrentRetries_CreateExactlyOneMessage()
    {
        var (factory, alice, _, chat) = await ArrangeAsync();
        await using var _ = factory;
        var clientMessageId = Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            using var client = factory.CreateClient(alice.Token);
            var response = await client.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text = "race", clientMessageId });
            return (response.StatusCode, Id: (await ReadJsonAsync(response)).GetProperty("id").GetGuid());
        }));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK }));
        Assert.Single(responses.Select(r => r.Id).Distinct());
        Assert.Equal(1, await CountMessagesAsync(chat));

        // Проигравшие ретраи не оставили дыр в нумерации.
        using var check = factory.CreateClient(alice.Token);
        var next = await ReadJsonAsync(await check.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text = "next" }));
        Assert.Equal(2, next.GetProperty("seq").GetInt64());
    }

    [Fact]
    public async Task ClientMessageId_OfAnotherChat_IsAConflict()
    {
        var (factory, alice, _, chat) = await ArrangeAsync();
        await using var _ = factory;
        var carol = await Data.UserAsync("carol");
        var otherChat = await Data.PrivateChatAsync(alice.UserId, carol);
        using var client = factory.CreateClient(alice.Token);
        var clientMessageId = Guid.NewGuid();

        (await client.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text = "here", clientMessageId })).EnsureSuccessStatusCode();
        var reused = await client.PostAsJsonAsync($"/api/chats/{otherChat}/messages", new { text = "there", clientMessageId });

        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        Assert.Equal("CLIENT_MESSAGE_ID_CONFLICT", (await ReadJsonAsync(reused)).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task ConcurrentSends_GetConsecutiveUniqueSeq()
    {
        var (factory, alice, bob, chat) = await ArrangeAsync();
        await using var _ = factory;

        var seqs = await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
        {
            using var client = factory.CreateClient(i % 2 == 0 ? alice.Token : bob.Token);
            var response = await client.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text = $"m{i}" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await ReadJsonAsync(response)).GetProperty("seq").GetInt64();
        }));

        Assert.Equal(Enumerable.Range(1, 20).Select(i => (long)i), seqs.Order());

        // История — строго по seq.
        using var reader = factory.CreateClient(alice.Token);
        var page = await ReadJsonAsync(await reader.GetAsync($"/api/chats/{chat}/messages/cursor?limit=100"));
        Assert.Equal(Enumerable.Range(1, 20).Select(i => (long)i),
            page.GetProperty("items").EnumerateArray().Select(m => m.GetProperty("seq").GetInt64()));
    }

    [Fact]
    public async Task CursorOfTheOldFormat_StillPagesFromTheSamePlace()
    {
        // Клиент получил курсор до обновления и листает дальше после него.
        var (factory, alice, bob, chat) = await ArrangeAsync();
        await using var _ = factory;
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
            ids.Add(await Data.MessageAsync(chat, i % 2 == 0 ? alice.UserId : bob.UserId, $"m{i}", TestData.T0.AddMinutes(i)));
        using var client = factory.CreateClient(alice.Token);

        // Старый курсор указывал на последнее сообщение страницы — m2.
        var legacy = MessageCursor.EncodeLegacy(TestData.T0.AddMinutes(2), ids[2]);
        var page = await ReadJsonAsync(await client.GetAsync($"/api/chats/{chat}/messages/cursor?cursor={legacy}"));

        Assert.Equal(["m0", "m1"], page.GetProperty("items").EnumerateArray().Select(m => m.GetProperty("text").GetString()));

        // Старый курсор на сообщение чужого чата — 400, а не страница.
        var foreign = MessageCursor.EncodeLegacy(TestData.T0, Guid.NewGuid());
        var rejected = await client.GetAsync($"/api/chats/{chat}/messages/cursor?cursor={foreign}");
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    [Fact]
    public async Task HubSend_AndRestSend_ShareOneSequence()
    {
        var (factory, alice, bob, chat) = await ArrangeAsync();
        await using var _ = factory;
        await using var hub = factory.CreateHubConnection(alice.Token);
        await hub.StartAsync();
        using var client = factory.CreateClient(bob.Token);

        await hub.InvokeAsync("SendMessage", chat, "over the hub");
        var rest = await ReadJsonAsync(await client.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text = "over REST" }));

        Assert.Equal(2, rest.GetProperty("seq").GetInt64());
        Assert.Equal(JsonValueKind.Null, rest.GetProperty("clientMessageId").ValueKind);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException();
            await Task.Delay(20);
        }
    }
}
