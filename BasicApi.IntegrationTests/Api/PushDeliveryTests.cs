using System.Net.Http.Json;
using BasicApi.IntegrationTests.Infrastructure;
using BasicApi.Services.Push;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace BasicApi.IntegrationTests.Api;

/// <summary>
/// Push notifications of new messages (plan 2, F7.3). The push service is replaced by a recorder.
/// Notifications go out one message after another, so "not notified of the first" is checked by
/// the next notification being of the second.
/// </summary>
public class PushDeliveryTests(PostgresFixture db) : DbTest(db)
{
    private readonly RecordingPushTransport _pushes = new();

    private ApiFactory Factory()
    {
        var settings = PushSubscriptionTests.PushOn();
        settings["RateLimiting:CommandsPer10Seconds"] = "1000";
        return new ApiFactory(Db.ConnectionString, settings, services: s =>
        {
            s.RemoveAll<IPushTransport>();
            s.AddSingleton<IPushTransport>(_pushes);
        });
    }

    private static async Task<PushTestDevice> SubscribeAsync(HttpClient api)
    {
        var device = new PushTestDevice();
        (await api.PutAsJsonAsync("/api/push/subscription", device.Subscription)).EnsureSuccessStatusCode();
        return device;
    }

    private static async Task<Guid> SendAsync(HttpClient api, Guid chatId, string text) =>
        (await api.PostJsonAsync($"/api/chats/{chatId}/messages", new { text })).Id();

    [Fact]
    public async Task OfflineMember_IsNotified_WithWhatToShow()
    {
        await using var factory = Factory();
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        using var bobPhone = await SubscribeAsync(bobApi);
        using var alicePhone = await SubscribeAsync(aliceApi);
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);

        var messageId = await SendAsync(aliceApi, chat, "Привет! " + new string('x', 150));

        var push = await _pushes.NextAsync();
        Assert.Equal(bobPhone.Endpoint, push.Endpoint); // not the sender's own device
        Assert.Equal(chat.ToString("N"), push.Topic);
        var n = push.Payload;
        Assert.Equal("message", n.GetProperty("kind").GetString());
        Assert.Equal(chat, n.Id("chatId"));
        Assert.Equal("private", n.GetProperty("chatType").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, n.GetProperty("chatTitle").ValueKind);
        Assert.Equal(messageId, n.Id("messageId"));
        Assert.Equal(1, n.GetProperty("seq").GetInt64());
        Assert.Equal(alice.UserId, n.Id("senderId"));
        Assert.Equal("alice", n.GetProperty("senderName").GetString());
        Assert.Equal("text", n.GetProperty("messageType").GetString());
        Assert.Equal(101, n.GetProperty("text").GetString()!.Length); // the preview: 100 and an ellipsis
        Assert.StartsWith("Привет!", n.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Group_NotifiesEveryOfflineMember_WithTheGroupsTitle()
    {
        await using var factory = Factory();
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carl = await factory.RegisterAsync("carl");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobPhone = await SubscribeAsync(factory.CreateClient(bob.Token));
        using var carlPhone = await SubscribeAsync(factory.CreateClient(carl.Token));
        var group = await Data.GroupChatAsync("Команда", [alice.UserId, bob.UserId, carl.UserId]);

        await SendAsync(aliceApi, group, "всем");

        var pushes = new[] { await _pushes.NextAsync(), await _pushes.NextAsync() };
        Assert.Equal(new[] { bobPhone.Endpoint, carlPhone.Endpoint }.Order(), pushes.Select(p => p.Endpoint).Order());
        Assert.All(pushes, p =>
        {
            Assert.Equal("group", p.Payload.GetProperty("chatType").GetString());
            Assert.Equal("Команда", p.Payload.GetProperty("chatTitle").GetString());
        });
    }

    [Fact]
    public async Task MemberWithTheAppOpen_IsNotNotified()
    {
        await using var factory = Factory();
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        using var bobPhone = await SubscribeAsync(bobApi);
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        await using var bobHub = factory.CreateHubConnection(bob.Token);
        await bobHub.StartAsync();

        var whileOpen = await SendAsync(aliceApi, chat, "you are here");
        await bobHub.StopAsync();
        await WaitUntilOfflineAsync(aliceApi, bob.UserId);
        var afterClosing = await SendAsync(aliceApi, chat, "you left");

        Assert.Equal(afterClosing, (await _pushes.NextAsync()).Payload.Id("messageId"));
        Assert.NotEqual(whileOpen, afterClosing);
    }

    [Fact]
    public async Task MutedChat_IsNotNotified_UntilTheMuteEnds()
    {
        await using var factory = Factory();
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        using var bobPhone = await SubscribeAsync(bobApi);
        var muted = await Data.GroupChatAsync("noisy", [alice.UserId, bob.UserId]);
        var other = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        (await bobApi.PutAsJsonAsync($"/api/chats/{muted}/muted", new { muted = true })).EnsureSuccessStatusCode();

        await SendAsync(aliceApi, muted, "quiet");
        var next = await SendAsync(aliceApi, other, "loud");
        Assert.Equal(next, (await _pushes.NextAsync()).Payload.Id("messageId"));

        // A mute that has run out no longer holds anything back.
        await using var connection = new NpgsqlConnection(Db.ConnectionString);
        await connection.ExecuteAsync("UPDATE chat_members SET muted_until = now() - interval '1 minute' WHERE chat_id = @muted",
            new { muted });
        var afterMute = await SendAsync(aliceApi, muted, "again");
        Assert.Equal(afterMute, (await _pushes.NextAsync()).Payload.Id("messageId"));
    }

    [Fact]
    public async Task BlockedSender_InAGroup_DoesNotNotify()
    {
        await using var factory = Factory();
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carl = await factory.RegisterAsync("carl");
        using var bobApi = factory.CreateClient(bob.Token);
        using var carlApi = factory.CreateClient(carl.Token);
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobPhone = await SubscribeAsync(bobApi);
        var group = await Data.GroupChatAsync("team", [alice.UserId, bob.UserId, carl.UserId]);
        (await bobApi.PutAsync($"/api/users/{carl.UserId}/block", null)).EnsureSuccessStatusCode();

        await SendAsync(carlApi, group, "from the blocked one");
        var fromAlice = await SendAsync(aliceApi, group, "from alice");

        Assert.Equal(fromAlice, (await _pushes.NextAsync()).Payload.Id("messageId"));
    }

    [Fact]
    public async Task SignedOutDevice_IsNotNotified()
    {
        await using var factory = Factory();
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var bobLaptop = await factory.LoginAsync("bob");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobPhone = await SubscribeAsync(factory.CreateClient(bob.Token));
        using var laptop = await SubscribeAsync(factory.CreateClient(bobLaptop.Token));
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        // The laptop's sign-in ends without logout (expired): its row is still there.
        await using var connection = new NpgsqlConnection(Db.ConnectionString);
        await connection.ExecuteAsync("UPDATE sessions SET expires_at = now() - interval '1 minute' WHERE family_id = @id",
            new { id = ApiClient.SessionFamilyOf(bobLaptop.Token) });

        await SendAsync(aliceApi, chat, "one");
        await SendAsync(aliceApi, chat, "two");

        Assert.Equal(bobPhone.Endpoint, (await _pushes.NextAsync()).Endpoint);
        Assert.Equal(bobPhone.Endpoint, (await _pushes.NextAsync()).Endpoint);
        Assert.DoesNotContain(_pushes.Drain(), p => p.Endpoint == laptop.Endpoint);
    }

    [Fact]
    public async Task SubscriptionThePushServiceForgot_IsRemoved()
    {
        await using var factory = Factory();
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        using var bobPhone = await SubscribeAsync(bobApi);
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        _pushes.Gone[bobPhone.Endpoint] = true;

        await SendAsync(aliceApi, chat, "anyone?");

        await using var connection = new NpgsqlConnection(Db.ConnectionString);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM devices WHERE push_endpoint IS NOT NULL") > 0)
        {
            Assert.True(DateTime.UtcNow < deadline, "the subscription was not removed");
            await Task.Delay(50);
        }
        var device = Assert.Single((await bobApi.GetJsonAsync("/api/devices")).GetProperty("items").EnumerateArray());
        Assert.False(device.GetProperty("pushEnabled").GetBoolean());
    }

    private static async Task WaitUntilOfflineAsync(HttpClient api, Guid userId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while ((await api.GetJsonAsync($"/api/users/{userId}/status")).GetProperty("isOnline").GetBoolean())
        {
            Assert.True(DateTime.UtcNow < deadline, "still online");
            await Task.Delay(50);
        }
    }
}
