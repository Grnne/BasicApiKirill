using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.SignalR.Client;

namespace BasicApi.IntegrationTests.Api;

/// <summary>Privacy settings, last seen and who sees whose presence (plan 2, F5.1, D11).</summary>
public class PrivacyTests(PostgresFixture db) : DbTest(db)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static Dictionary<string, string?> Settings => new() { ["RateLimiting:CommandsPer10Seconds"] = "1000" };

    /// <summary>A hub connection that records who went online and offline.</summary>
    private sealed class Presence : IAsyncDisposable
    {
        private readonly HubConnection _hub;
        public ConcurrentQueue<(Guid UserId, bool IsOnline)> Seen { get; } = new();

        public Presence(ApiFactory factory, string token)
        {
            _hub = factory.CreateHubConnection(token);
            _hub.On<Guid, bool>("UserOnlineChanged", (id, online) => Seen.Enqueue((id, online)));
        }

        public Task StartAsync() => _hub.StartAsync();
        public Task StopAsync() => _hub.StopAsync();

        public async Task<bool> WaitForAsync(Guid userId, bool isOnline)
        {
            var until = DateTime.UtcNow + Wait;
            while (DateTime.UtcNow < until)
            {
                if (Seen.Contains((userId, isOnline))) return true;
                await Task.Delay(50);
            }
            return false;
        }

        public ValueTask DisposeAsync() => _hub.DisposeAsync();
    }

    private static Task<HttpResponseMessage> PutPrivacyAsync(HttpClient api, object body) =>
        api.PutAsJsonAsync("/api/users/me/privacy", body);

    [Fact]
    public async Task Settings_StartOpen_ChangeByField_AndReachTheOtherDevices()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        using var api = factory.CreateClient(alice.Token);

        var initial = await api.GetJsonAsync("/api/users/me/privacy");
        Assert.Equal("everybody", initial.GetProperty("lastSeen").GetString());
        Assert.Equal("everybody", initial.GetProperty("messages").GetString());
        Assert.Equal("everybody", initial.GetProperty("groupAdd").GetString());

        var changed = await (await PutPrivacyAsync(api, new { messages = "contacts" })).ReadJsonAsync();
        Assert.Equal("contacts", changed.GetProperty("messages").GetString());
        Assert.Equal("everybody", changed.GetProperty("lastSeen").GetString());
        // The same again changes nothing.
        await PutPrivacyAsync(api, new { messages = "contacts" });

        Assert.Equal("INVALID_PRIVACY", await (await PutPrivacyAsync(api, new { groupAdd = "friends" })).ErrorCodeAsync());
        var update = Assert.Single(await api.JournalAsync("PrivacyUpdated"));
        Assert.Equal("contacts", update.GetProperty("messages").GetString());
    }

    [Fact]
    public async Task LastSeen_IsKept_AndShownToContacts()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carl = await factory.RegisterAsync("carl");
        using var bobApi = factory.CreateClient(bob.Token);
        using var carlApi = factory.CreateClient(carl.Token);
        await Data.PrivateChatAsync(alice.UserId, bob.UserId);

        var before = DateTime.UtcNow.AddSeconds(-1);
        // The hub finishes connecting and disconnecting after the client call returns: wait for both.
        async Task<JsonElement> StatusWhen(Func<JsonElement, bool> done)
        {
            var status = await bobApi.GetJsonAsync($"/api/users/{alice.UserId}/status");
            for (var i = 0; i < 100 && !done(status); i++)
            {
                await Task.Delay(50);
                status = await bobApi.GetJsonAsync($"/api/users/{alice.UserId}/status");
            }
            return status;
        }
        await using (var hub = factory.CreateHubConnection(alice.Token))
        {
            await hub.StartAsync();
            Assert.True((await StatusWhen(s => s.GetProperty("isOnline").GetBoolean())).GetProperty("isOnline").GetBoolean());
            await hub.StopAsync();
        }

        var status = await StatusWhen(s => !s.GetProperty("isOnline").GetBoolean());
        Assert.False(status.GetProperty("isOnline").GetBoolean());
        Assert.InRange(status.GetProperty("lastSeenAt").GetDateTime().ToUniversalTime(), before, DateTime.UtcNow.AddSeconds(1));
        // Not a contact: not even whether the account exists.
        Assert.Equal(HttpStatusCode.NotFound, (await carlApi.GetAsync($"/api/users/{alice.UserId}/status")).StatusCode);
    }

    [Fact]
    public async Task HiddenPresence_IsHiddenBothWays_AndTakesEffectAtOnce()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        await using var aliceHub = new Presence(factory, alice.Token);
        await using var bobHub = new Presence(factory, bob.Token);
        await aliceHub.StartAsync();
        await bobHub.StartAsync();
        Assert.True(await bobHub.WaitForAsync(alice.UserId, true));

        // Alice hides: Bob sees her go offline, and she stops seeing him.
        await PutPrivacyAsync(aliceApi, new { lastSeen = "nobody" });
        Assert.True(await bobHub.WaitForAsync(alice.UserId, false));
        Assert.True(await aliceHub.WaitForAsync(bob.UserId, false));

        var aliceSeenByBob = await bobApi.GetJsonAsync($"/api/users/{alice.UserId}/status");
        Assert.False(aliceSeenByBob.GetProperty("isOnline").GetBoolean());
        Assert.Equal(JsonValueKind.Null, aliceSeenByBob.GetProperty("lastSeenAt").ValueKind);
        var bobSeenByAlice = await aliceApi.GetJsonAsync($"/api/users/{bob.UserId}/status");
        Assert.False(bobSeenByAlice.GetProperty("isOnline").GetBoolean());
        Assert.Empty((await bobApi.GetJsonAsync("/api/users/status")).GetProperty("items").EnumerateArray());

        // Reconnecting while hidden announces nothing.
        bobHub.Seen.Clear();
        await using (var laptop = factory.CreateHubConnection((await factory.LoginAsync("alice")).Token))
            await laptop.StartAsync();
        await Task.Delay(300);
        Assert.DoesNotContain(bobHub.Seen, e => e.UserId == alice.UserId);

        // Shown again: both appear online to each other.
        await PutPrivacyAsync(aliceApi, new { lastSeen = "contacts" });
        Assert.True(await bobHub.WaitForAsync(alice.UserId, true));
        Assert.True(await aliceHub.WaitForAsync(bob.UserId, true));
        Assert.True((await bobApi.GetJsonAsync($"/api/users/{alice.UserId}/status")).GetProperty("isOnline").GetBoolean());
    }
}
