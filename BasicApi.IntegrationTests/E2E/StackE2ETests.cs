using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace BasicApi.IntegrationTests.E2E;

/// <summary>Waits for hub events by a condition — events arrive asynchronously.</summary>
internal sealed class HubEvents
{
    private readonly ConcurrentQueue<(string Name, object?[] Args)> _events = new();
    private readonly SemaphoreSlim _signal = new(0);

    public void Record(HubConnection hub, string name, params Type[] argTypes) =>
        hub.On(name, argTypes, args =>
        {
            _events.Enqueue((name, args));
            _signal.Release();
            return Task.CompletedTask;
        });

    public async Task<object?[]> WaitAsync(string name, Func<object?[], bool>? match = null, int seconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (true)
        {
            var hit = _events.FirstOrDefault(e => e.Name == name && (match?.Invoke(e.Args) ?? true));
            if (hit.Name is not null)
                return hit.Args;

            var left = deadline - DateTime.UtcNow;
            if (left <= TimeSpan.Zero || !await _signal.WaitAsync(left))
                throw new TimeoutException($"hub event {name} did not arrive in {seconds} s");
        }
    }
}

[Trait("Category", "E2E")]
[Collection(E2ECollection.Name)]
public class StackE2ETests(E2EUsers users)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [E2EFact]
    public async Task Stack_IsHealthy_BehindTls_WithSecurityHeaders()
    {
        using var client = E2EEnvironment.CreateClient();

        var ready = await client.GetAsync("health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("Healthy", await ready.Content.ReadAsStringAsync());

        var page = await client.GetAsync("client/");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("<div id=\"app\">", await page.Content.ReadAsStringAsync());
        Assert.Contains("frame-ancestors 'none'", page.Headers.GetValues("Content-Security-Policy").Single());
        Assert.StartsWith("max-age=", page.Headers.GetValues("Strict-Transport-Security").Single()); // from Caddy
        Assert.False(page.Headers.Contains("Server"));
    }

    [E2EFact]
    public async Task Prod_HidesSwagger_AndRequiresAuth()
    {
        using var client = E2EEnvironment.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("swagger/v1/swagger.json")).StatusCode);

        var chats = await client.GetAsync("api/chats");
        Assert.Equal(HttpStatusCode.Unauthorized, chats.StatusCode);
        var problem = await chats.ReadAsync<JsonElement>();
        Assert.Equal("TOKEN_MISSING_OR_EXPIRED", problem.GetProperty("errorCode").GetString());
        Assert.False(problem.TryGetProperty("stackTrace", out _));
    }

    [E2EFact]
    public async Task Conversation_EndToEnd()
    {
        var alice = users.Alice;
        var bob = users.Bob;
        using var aliceApi = E2EEnvironment.CreateClient(alice.Token);
        using var bobApi = E2EEnvironment.CreateClient(bob.Token);

        await using var aliceHub = E2EEnvironment.CreateHub(alice.Token);
        await using var bobHub = E2EEnvironment.CreateHub(bob.Token);
        var aliceEvents = new HubEvents();
        var bobEvents = new HubEvents();
        foreach (var (hub, events) in new[] { (aliceHub, aliceEvents), (bobHub, bobEvents) })
        {
            events.Record(hub, "UserOnlineChanged", typeof(Guid), typeof(bool));
            events.Record(hub, "ChatCreated", typeof(JsonElement));
            events.Record(hub, "MessageCreated", typeof(JsonElement));
            events.Record(hub, "ChatListUpdated", typeof(Guid), typeof(JsonElement));
            events.Record(hub, "TypingChanged", typeof(Guid), typeof(Guid), typeof(bool));
        }
        await aliceHub.StartAsync();
        await bobHub.StartAsync();

        // Login is case-insensitive and finds Bob
        var lookup = await aliceApi.GetAsync($"api/users/GetUserId/{bob.Username.ToUpperInvariant()}");
        Assert.Equal(bob.UserId, (await lookup.ReadAsync<JsonElement>()).GetProperty("userId").GetGuid());

        // New personal chat: Bob gets ChatCreated, both get the counterpart's online status
        var created = await aliceApi.PostAsync($"api/chats/private/{bob.UserId}", null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var chatId = (await created.ReadAsync<JsonElement>()).GetProperty("chatId").GetGuid();
        await bobEvents.WaitAsync("ChatCreated", a => ((JsonElement)a[0]!).GetProperty("chatId").GetGuid() == chatId);
        await aliceEvents.WaitAsync("UserOnlineChanged", a => (Guid)a[0]! == bob.UserId && (bool)a[1]!);
        await bobEvents.WaitAsync("UserOnlineChanged", a => (Guid)a[0]! == alice.UserId && (bool)a[1]!);

        // Creating it again — the same chat
        var again = await bobApi.PostAsync($"api/chats/private/{alice.UserId}", null);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(chatId, (await again.ReadAsync<JsonElement>()).GetProperty("chatId").GetGuid());

        await aliceHub.InvokeAsync("JoinChat", chatId);
        await bobHub.InvokeAsync("JoinChat", chatId);

        // Typing…
        await bobHub.InvokeAsync("Typing", chatId, true);
        await aliceEvents.WaitAsync("TypingChanged", a => (Guid)a[1]! == bob.UserId && (bool)a[2]!);

        // Messages: whitespace trimming, real-time delivery
        await aliceHub.InvokeAsync("SendMessage", chatId, "   hello from alice   ");
        var delivered = (JsonElement)(await bobEvents.WaitAsync("MessageCreated"))[0]!;
        Assert.Equal("hello from alice", delivered.GetProperty("text").GetString());
        await bobEvents.WaitAsync("ChatListUpdated", a => (Guid)a[0]! == chatId);

        // The same send via the REST command — the event arrives the same way
        var sent = await bobApi.PostAsJsonAsync($"api/chats/{chatId}/messages", new { text = "hello back" });
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        await aliceEvents.WaitAsync("MessageCreated", a => ((JsonElement)a[0]!).GetProperty("text").GetString() == "hello back");

        // Unread: own messages don't count
        async Task<int> UnreadAsync(HttpClient api) =>
            (await (await api.GetAsync("api/chats")).ReadAsync<JsonElement>()).EnumerateArray()
                .Single(c => c.GetProperty("chatId").GetGuid() == chatId).GetProperty("unreadCount").GetInt32();
        Assert.Equal(1, await UnreadAsync(aliceApi));
        Assert.Equal(1, await UnreadAsync(bobApi));

        // Read moves forward; it does not roll back
        var history = await (await bobApi.GetAsync($"api/chats/{chatId}/messages/cursor?limit=10")).ReadAsync<JsonElement>();
        var items = history.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["hello from alice", "hello back"], items.Select(m => m.GetProperty("text").GetString()));
        Assert.False(history.GetProperty("hasMore").GetBoolean());
        Assert.Equal(JsonValueKind.Null, history.GetProperty("nextCursor").ValueKind);
        var aliceMessageId = items[0].GetProperty("id").GetGuid();
        (await bobApi.PostAsJsonAsync($"api/chats/{chatId}/read", new { lastMessageId = aliceMessageId })).EnsureSuccessStatusCode();
        Assert.Equal(0, await UnreadAsync(bobApi));

        // Jump to date: the last moment — the whole history, including the last message
        var at = await (await bobApi.GetAsync(
            $"api/chats/{chatId}/messages/at?date={Uri.EscapeDataString(DateTime.UtcNow.AddMinutes(1).ToString("O"))}&limit=10"))
            .ReadAsync<JsonElement>();
        Assert.Equal(2, at.GetProperty("items").GetArrayLength());

        // Message search
        var search = await (await aliceApi.GetAsync($"api/chats/{chatId}/messages/search?q=hello")).ReadAsync<JsonElement>();
        Assert.Equal(2, search.GetProperty("totalCount").GetInt32());

        // Broken cursor — 400, not 500
        var broken = await aliceApi.GetAsync($"api/chats/{chatId}/messages/cursor?cursor=garbage!!");
        Assert.Equal(HttpStatusCode.BadRequest, broken.StatusCode);

        // Sync: Bob's journal has the new chat, two messages and his reading (for his other devices), in order
        var state = await (await bobApi.GetAsync("api/sync/state")).ReadAsync<JsonElement>();
        Assert.Equal(4, state.GetProperty("pts").GetInt64());
        var missed = await (await bobApi.GetAsync("api/sync?since=1")).ReadAsync<JsonElement>();
        var updates = missed.GetProperty("updates").EnumerateArray().ToList();
        Assert.Equal(["MessageCreated", "MessageCreated", "ReadStateChanged"], updates.Select(u => u.GetProperty("type").GetString()));
        Assert.Equal(["hello from alice", "hello back"], updates.Take(2)
            .Select(u => u.GetProperty("payload").GetProperty("text").GetString()));
        Assert.Equal(0, updates[2].GetProperty("payload").GetProperty("unreadCount").GetInt32());
    }

    [E2EFact]
    public async Task Hub_ReportsErrorsWithCodes()
    {
        await using var hub = E2EEnvironment.CreateHub(users.Alice.Token);
        await hub.StartAsync();

        // On the wire SignalR adds its own prefix — the code is extracted the same way as in the client.
        static string Code(HubException ex) =>
            System.Text.RegularExpressions.Regex.Match(ex.Message, "HubException: ([A-Z_]+):").Groups[1].Value;

        var foreign = await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync("JoinChat", Guid.NewGuid()));
        Assert.Equal("NOT_A_MEMBER", Code(foreign));

        var empty = await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync("SendMessage", Guid.NewGuid(), "   "));
        Assert.Equal("MESSAGE_EMPTY", Code(empty));
    }

    [E2EFact]
    public async Task Logout_ClosesThatDevicesConnection_OnlyIt()
    {
        var laptop = await E2EUsers.LoginAsync(users.Alice.Username.ToLowerInvariant()); // case-insensitive login
        await using var laptopHub = E2EEnvironment.CreateHub(laptop.Token);
        await using var phoneHub = E2EEnvironment.CreateHub(users.Alice.Token);
        var laptopClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        laptopHub.Closed += _ =>
        {
            laptopClosed.TrySetResult();
            return Task.CompletedTask;
        };
        await laptopHub.StartAsync();
        await phoneHub.StartAsync();

        using var api = E2EEnvironment.CreateClient(laptop.Token);
        (await api.PostAsJsonAsync("api/auth/logout", new { refreshToken = laptop.RefreshToken })).EnsureSuccessStatusCode();

        await laptopClosed.Task.WaitAsync(Timeout);
        Assert.Equal(HubConnectionState.Connected, phoneHub.State);

        // The old token of the logged-out device cannot reconnect
        await using var retry = E2EEnvironment.CreateHub(laptop.Token);
        var retryClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        retry.Closed += _ =>
        {
            retryClosed.TrySetResult();
            return Task.CompletedTask;
        };
        try
        {
            await retry.StartAsync();
            await retryClosed.Task.WaitAsync(Timeout);
        }
        catch (Exception ex) when (ex is not TimeoutException)
        {
            // failing right at startup is also correct
        }
        Assert.NotEqual(HubConnectionState.Connected, retry.State);
    }
}
