using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.SignalR.Client;

namespace BasicApi.IntegrationTests.Platform;

/// <summary>Commands over REST: the same services and events as the hub methods; the hub remains the events channel.</summary>
public class CommandsApiTests(PostgresFixture db) : DbTest(db)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await ReadJsonAsync(response)).GetProperty("errorCode").GetString();

    [Fact]
    public async Task SendMessage_ViaRest_IsStored_AndDeliveredOverTheHub()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);

        var created = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listUpdated = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var bobHub = factory.CreateHubConnection(bob.Token);
        bobHub.On<JsonElement>("MessageCreated", m => created.TrySetResult(m));
        bobHub.On<Guid, JsonElement>("ChatListUpdated", (_, m) => listUpdated.TrySetResult(m));
        await bobHub.StartAsync();
        await bobHub.InvokeAsync("JoinChat", chat);

        using var client = factory.CreateClient(alice.Token);
        var response = await client.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text = "  hello over REST  " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("hello over REST", body.GetProperty("text").GetString());
        Assert.Equal(alice.UserId, body.GetProperty("senderId").GetGuid());
        Assert.Equal("alice", body.GetProperty("senderName").GetString());

        var delivered = await created.Task.WaitAsync(Wait);
        Assert.Equal(body.GetProperty("id").GetGuid(), delivered.GetProperty("id").GetGuid());
        Assert.Equal("hello over REST", (await listUpdated.Task.WaitAsync(Wait)).GetProperty("text").GetString());

        var history = await ReadJsonAsync(await client.GetAsync($"/api/chats/{chat}/messages/cursor"));
        Assert.Equal("hello over REST", history.GetProperty("items")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task SendMessage_ViaRest_ErrorsHaveTheSameCodesAsTheHub()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carol = await factory.RegisterAsync("carol");
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        using var aliceClient = factory.CreateClient(alice.Token);
        using var carolClient = factory.CreateClient(carol.Token);

        var empty = await aliceClient.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal("MESSAGE_EMPTY", await ErrorCodeAsync(empty));

        var noText = await aliceClient.PostAsJsonAsync($"/api/chats/{chat}/messages", new { });
        Assert.Equal("MESSAGE_EMPTY", await ErrorCodeAsync(noText));

        var tooLong = await aliceClient.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text = new string('x', 4097) });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal("MESSAGE_TOO_LONG", await ErrorCodeAsync(tooLong));

        var foreign = await carolClient.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text = "hi" });
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal("NOT_A_MEMBER", await ErrorCodeAsync(foreign));
    }

    [Fact]
    public async Task Typing_ViaRest_ReachesTheOtherMember()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);

        var typing = new TaskCompletionSource<(Guid Chat, Guid User, bool IsTyping)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var bobHub = factory.CreateHubConnection(bob.Token);
        bobHub.On<Guid, Guid, bool>("TypingChanged", (c, u, t) => typing.TrySetResult((c, u, t)));
        await bobHub.StartAsync();

        using var client = factory.CreateClient(alice.Token);
        var response = await client.PostAsJsonAsync($"/api/chats/{chat}/typing", new { isTyping = true });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal((chat, alice.UserId, true), await typing.Task.WaitAsync(Wait));

        using var bobClient = factory.CreateClient(bob.Token);
        var snapshot = await ReadJsonAsync(await bobClient.GetAsync("/api/users/typing"));
        Assert.Equal(alice.UserId, snapshot.GetProperty("items")[0].GetProperty("userId").GetGuid());
    }

    [Fact]
    public async Task Commands_AreLimitedPerUser()
    {
        await using var factory = new ApiFactory(Db.ConnectionString,
            new Dictionary<string, string?> { ["RateLimiting:CommandsPer10Seconds"] = "3" });
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        using var alicePhone = factory.CreateClient(alice.Token);
        using var bobClient = factory.CreateClient(bob.Token);

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Created,
                (await alicePhone.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text = $"m{i}" })).StatusCode);

        // A second device of the same user shares the same budget — the limit is per user.
        var alice2 = await factory.LoginAsync("alice");
        using var aliceLaptop = factory.CreateClient(alice2.Token);
        var limited = await aliceLaptop.PostAsJsonAsync($"/api/chats/{chat}/typing", new { isTyping = true });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("RATE_LIMITED", await ErrorCodeAsync(limited));

        // Another user has their own budget.
        Assert.Equal(HttpStatusCode.Created,
            (await bobClient.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text = "bob" })).StatusCode);
    }
}
