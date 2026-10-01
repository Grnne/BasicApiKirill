using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.SignalR.Client;

namespace BasicApi.IntegrationTests.Features.Messages;

/// <summary>"Sent / delivered / read".</summary>
public class ReceiptsTests(PostgresFixture db) : DbTest(db)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static Dictionary<string, string?> Settings => new() { ["RateLimiting:CommandsPer10Seconds"] = "1000" };

    private static async Task<Guid> SendAsync(HttpClient client, Guid chatId, string text) =>
        (await client.PostJsonAsync($"/api/chats/{chatId}/messages", new { text })).Id();

    private static async Task AckAllAsync(HttpClient client) =>
        (await client.PostAsJsonAsync("/api/sync/ack", new { pts = await client.PtsAsync() })).EnsureSuccessStatusCode();

    private static async Task ReadAsync(HttpClient client, Guid chatId, Guid messageId) =>
        (await client.PostAsJsonAsync($"/api/chats/{chatId}/read", new { lastMessageId = messageId })).EnsureSuccessStatusCode();

    private static async Task<JsonElement> MessageAsync(HttpClient client, Guid chatId, Guid messageId) =>
        (await client.HistoryAsync(chatId)).Single(m => m.Id() == messageId);

    private static string? Status(JsonElement message) => message.GetProperty("status").GetString();

    [Fact]
    public async Task PrivateChat_MessageGoesSentThenDeliveredThenRead_AndTheAuthorHearsOfEachStep()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        var chat = (await aliceApi.PostJsonAsync($"/api/chats/private/{bob.UserId}")).Id("chatId");

        var receipts = new List<(string Event, JsonElement Payload)>();
        var gotRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var aliceHub = factory.CreateHubConnection(alice.Token);
        aliceHub.On<JsonElement>("MessagesDelivered", r => { lock (receipts) receipts.Add(("MessagesDelivered", r)); });
        aliceHub.On<JsonElement>("MessagesRead", r =>
        {
            lock (receipts) receipts.Add(("MessagesRead", r));
            gotRead.TrySetResult();
        });
        await aliceHub.StartAsync();

        var first = await SendAsync(aliceApi, chat, "one");
        var second = await SendAsync(aliceApi, chat, "two");
        Assert.Equal("sent", Status(await MessageAsync(aliceApi, chat, second)));
        Assert.Equal(0, (await aliceApi.ChatItemAsync(chat)).GetProperty("outboxDeliveredSeq").GetInt64());

        // Bob's device received both through its journal.
        await AckAllAsync(bobApi);
        var afterAck = await MessageAsync(aliceApi, chat, second);
        Assert.Equal("delivered", Status(afterAck));
        Assert.False(afterAck.GetProperty("isRead").GetBoolean());
        var item = await aliceApi.ChatItemAsync(chat);
        Assert.Equal(afterAck.GetProperty("seq").GetInt64(), item.GetProperty("outboxDeliveredSeq").GetInt64());
        Assert.Equal("delivered", item.GetProperty("lastMessage").GetProperty("status").GetString());

        // Bob reads the first one: only it is read.
        await ReadAsync(bobApi, chat, first);
        Assert.Equal("read", Status(await MessageAsync(aliceApi, chat, first)));
        Assert.Equal("delivered", Status(await MessageAsync(aliceApi, chat, second)));
        await gotRead.Task.WaitAsync(Wait);

        // For Bob these are someone else's messages: no status, isRead — his own reading.
        var bobsView = await bobApi.HistoryAsync(chat);
        Assert.All(bobsView, m => Assert.Equal(JsonValueKind.Null, m.GetProperty("status").ValueKind));
        Assert.Equal([true, false], bobsView.Select(m => m.GetProperty("isRead").GetBoolean()));

        var delivered = Assert.Single(await aliceApi.JournalAsync("MessagesDelivered"));
        Assert.Equal(chat, delivered.Id("chatId"));
        Assert.Equal(bob.UserId, delivered.Id("userId"));
        Assert.Equal(afterAck.GetProperty("seq").GetInt64(), delivered.GetProperty("seq").GetInt64());
        var read = Assert.Single(await aliceApi.JournalAsync("MessagesRead"));
        Assert.Equal((await MessageAsync(aliceApi, chat, first)).GetProperty("seq").GetInt64(), read.GetProperty("seq").GetInt64());
        lock (receipts)
            Assert.Equal(["MessagesDelivered", "MessagesRead"], receipts.Select(r => r.Event));

        // Bob hears nothing about his own reading and receiving.
        Assert.Empty(await bobApi.JournalAsync("MessagesDelivered"));
        Assert.Empty(await bobApi.JournalAsync("MessagesRead"));
    }

    [Fact]
    public async Task Reading_WithoutAck_MakesMessagesDeliveredToo()
    {
        // The current web client never acknowledges: its messages go from "sent" straight to "read".
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        var message = await SendAsync(aliceApi, chat, "hi");

        await ReadAsync(bobApi, chat, message);

        var item = await aliceApi.ChatItemAsync(chat);
        Assert.Equal(item.GetProperty("outboxReadSeq").GetInt64(), item.GetProperty("outboxDeliveredSeq").GetInt64());
        Assert.True(item.GetProperty("lastMessage").GetProperty("isRead").GetBoolean());
        Assert.Equal("read", item.GetProperty("lastMessage").GetProperty("status").GetString());
        Assert.Equal(item.GetProperty("lastMessage").GetProperty("seq").GetInt64(),
            (await bobApi.ChatItemAsync(chat)).GetProperty("lastReadSeq").GetInt64());
    }

    [Fact]
    public async Task Group_StatusChangesOnce_TheFirstMemberToReceiveOrReadDecides()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carol = await factory.RegisterAsync("carol");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        using var carolApi = factory.CreateClient(carol.Token);
        var chat = await Data.GroupChatAsync("team", [alice.UserId, bob.UserId, carol.UserId]);
        var fromAlice = await SendAsync(aliceApi, chat, "from alice");
        var fromBob = await SendAsync(bobApi, chat, "from bob");

        await AckAllAsync(carolApi);
        await AckAllAsync(bobApi);
        await ReadAsync(carolApi, chat, fromBob);
        await ReadAsync(bobApi, chat, fromBob);

        // Each author heard once of each status: from Carol, who was first. Bob's own message
        // did not make him an addressee of his own reading.
        Assert.Equal([carol.UserId], (await aliceApi.JournalAsync("MessagesDelivered")).Select(r => r.Id("userId")));
        Assert.Equal([carol.UserId], (await aliceApi.JournalAsync("MessagesRead")).Select(r => r.Id("userId")));
        Assert.Equal([carol.UserId], (await bobApi.JournalAsync("MessagesDelivered")).Select(r => r.Id("userId")));
        Assert.Equal([carol.UserId], (await bobApi.JournalAsync("MessagesRead")).Select(r => r.Id("userId")));
        Assert.Empty(await carolApi.JournalAsync("MessagesRead"));

        Assert.Equal("read", Status(await MessageAsync(aliceApi, chat, fromAlice)));
        Assert.Equal("read", Status(await MessageAsync(bobApi, chat, fromBob)));
    }

    [Fact]
    public async Task Ack_OfAnOlderPts_ByAnotherDevice_ChangesNothing()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var bobLaptop = await factory.LoginAsync("bob");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobPhone = factory.CreateClient(bob.Token);
        using var bobLaptopApi = factory.CreateClient(bobLaptop.Token);
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        await SendAsync(aliceApi, chat, "one");
        var early = await bobPhone.PtsAsync();
        var second = await SendAsync(aliceApi, chat, "two");

        await AckAllAsync(bobPhone);
        (await bobLaptopApi.PostAsJsonAsync("/api/sync/ack", new { pts = early })).EnsureSuccessStatusCode();

        Assert.Equal("delivered", Status(await MessageAsync(aliceApi, chat, second)));
        Assert.Single(await aliceApi.JournalAsync("MessagesDelivered"));
    }

    [Fact]
    public async Task ConcurrentAcksAndReads_KeepThePointersForwardOnly()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var devices = new List<HttpClient> { factory.CreateClient(bob.Token) };
        for (var i = 0; i < 3; i++)
            devices.Add(factory.CreateClient((await factory.LoginAsync("bob")).Token));
        using var aliceApi = factory.CreateClient(alice.Token);
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        var ids = new List<Guid>();
        for (var i = 0; i < 10; i++)
            ids.Add(await SendAsync(aliceApi, chat, $"m{i}"));

        await Task.WhenAll(devices.Select((d, i) => Task.Run(async () =>
        {
            await AckAllAsync(d);
            await ReadAsync(d, chat, ids[5 + i]);
        })));

        var item = await aliceApi.ChatItemAsync(chat);
        var history = await aliceApi.HistoryAsync(chat);
        Assert.Equal(history[8].GetProperty("seq").GetInt64(), item.GetProperty("outboxReadSeq").GetInt64());
        Assert.Equal(history[9].GetProperty("seq").GetInt64(), item.GetProperty("outboxDeliveredSeq").GetInt64());
        Assert.Equal(["read", "read", "read", "read", "read", "read", "read", "read", "read", "delivered"],
            history.Select(Status));
        foreach (var d in devices) d.Dispose();
    }

    [Fact]
    public async Task SavedMessages_HaveNoStatus()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        using var aliceApi = factory.CreateClient(alice.Token);
        var chat = (await aliceApi.PostJsonAsync("/api/chats/saved")).Id("chatId");
        var note = await SendAsync(aliceApi, chat, "note");
        await AckAllAsync(aliceApi);

        Assert.Equal(JsonValueKind.Null, (await MessageAsync(aliceApi, chat, note)).GetProperty("status").ValueKind);
        Assert.Equal(JsonValueKind.Null,
            (await aliceApi.ChatItemAsync(chat)).GetProperty("lastMessage").GetProperty("status").ValueKind);
        Assert.Empty(await aliceApi.JournalAsync("MessagesDelivered"));
    }

    [Fact]
    public async Task MessagesDeletedForEveryone_DoNotWakeTheirAuthor()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        var gone = await SendAsync(aliceApi, chat, "oops");
        Assert.Equal(HttpStatusCode.NoContent,
            (await aliceApi.DeleteAsync($"/api/chats/{chat}/messages/{gone}?forEveryone=true")).StatusCode);

        await AckAllAsync(bobApi);

        Assert.Empty(await aliceApi.JournalAsync("MessagesDelivered"));
    }
}
