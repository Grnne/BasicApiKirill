using System.Net;
using System.Net.Http.Json;
using BasicApi.IntegrationTests.Infrastructure;
using BasicApi.Services.Push;
using Dapper;
using Npgsql;

namespace BasicApi.IntegrationTests.Api;

/// <summary>WebPush subscriptions, one per device (plan 2, F7.2).</summary>
public class PushSubscriptionTests(PostgresFixture db) : DbTest(db)
{
    public static Dictionary<string, string?> PushOn()
    {
        var (publicKey, privateKey) = VapidKeys.Generate();
        return new()
        {
            ["Push:VapidPublicKey"] = publicKey,
            ["Push:VapidPrivateKey"] = privateKey,
            ["Push:Subject"] = "mailto:admin@test.local"
        };
    }

    private async Task<List<(Guid Id, string? Endpoint)>> SubscriptionsAsync()
    {
        await using var connection = new NpgsqlConnection(Db.ConnectionString);
        return [.. await connection.QueryAsync<(Guid, string?)>("SELECT id, push_endpoint FROM devices ORDER BY created_at")];
    }

    [Fact]
    public async Task WithoutKeys_PushIsOff()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        var alice = await factory.RegisterAsync("alice");
        using var api = factory.CreateClient(alice.Token);
        using var device = new PushTestDevice();

        var config = await api.GetJsonAsync("/api/push/config");
        Assert.False(config.GetProperty("enabled").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, config.GetProperty("vapidPublicKey").ValueKind);

        var response = await api.PutAsJsonAsync("/api/push/subscription", device.Subscription);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("PUSH_UNAVAILABLE", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task Subscription_BelongsToTheDevice_AndCanBeRemoved()
    {
        var settings = PushOn();
        await using var factory = new ApiFactory(Db.ConnectionString, settings);
        var phone = await factory.RegisterAsync("alice");
        var laptop = await factory.LoginAsync("alice");
        using var phoneApi = factory.CreateClient(phone.Token);
        using var laptopApi = factory.CreateClient(laptop.Token);
        using var device = new PushTestDevice();

        var config = await phoneApi.GetJsonAsync("/api/push/config");
        Assert.True(config.GetProperty("enabled").GetBoolean());
        Assert.Equal(settings["Push:VapidPublicKey"], config.GetProperty("vapidPublicKey").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await phoneApi.PutAsJsonAsync("/api/push/subscription", device.Subscription)).StatusCode);

        var devices = (await laptopApi.GetJsonAsync("/api/devices")).GetProperty("items").EnumerateArray().ToList();
        Assert.True(devices.Single(d => d.Id() == ApiClient.SessionFamilyOf(phone.Token)).GetProperty("pushEnabled").GetBoolean());
        Assert.False(devices.Single(d => d.Id() == ApiClient.SessionFamilyOf(laptop.Token)).GetProperty("pushEnabled").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await phoneApi.DeleteAsync("/api/push/subscription")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await phoneApi.DeleteAsync("/api/push/subscription")).StatusCode);
        Assert.All(await SubscriptionsAsync(), s => Assert.Null(s.Endpoint));
    }

    [Fact]
    public async Task SameBrowser_NextSignIn_TakesTheSubscriptionOver()
    {
        // Alice logs out without unsubscribing (or her sign-in just stays), Bob signs in in the
        // same browser and subscribes: the browser gives him the same endpoint. Alice's
        // notifications must not reach Bob's screen.
        await using var factory = new ApiFactory(Db.ConnectionString, PushOn());
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var device = new PushTestDevice();
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        (await aliceApi.PutAsJsonAsync("/api/push/subscription", device.Subscription)).EnsureSuccessStatusCode();

        (await bobApi.PutAsJsonAsync("/api/push/subscription", device.Subscription)).EnsureSuccessStatusCode();

        var subscriptions = await SubscriptionsAsync();
        Assert.Equal(device.Endpoint, subscriptions.Single(s => s.Id == ApiClient.SessionFamilyOf(bob.Token)).Endpoint);
        Assert.Null(subscriptions.Single(s => s.Id == ApiClient.SessionFamilyOf(alice.Token)).Endpoint);

        // Subscribing again changes nothing.
        (await bobApi.PutAsJsonAsync("/api/push/subscription", device.Subscription)).EnsureSuccessStatusCode();
        Assert.Single(await SubscriptionsAsync(), s => s.Endpoint == device.Endpoint);
    }

    [Fact]
    public async Task Logout_RemovesTheSubscription_AndAStaleTokenCannotAddOne()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, PushOn());
        var alice = await factory.RegisterAsync("alice");
        using var api = factory.CreateClient(alice.Token);
        using var device = new PushTestDevice();
        (await api.PutAsJsonAsync("/api/push/subscription", device.Subscription)).EnsureSuccessStatusCode();

        (await api.PostAsJsonAsync("/api/auth/logout", new { refreshToken = alice.RefreshToken })).EnsureSuccessStatusCode();

        Assert.Empty(await SubscriptionsAsync());
        // The access token still works for minutes, but the sign-in is over.
        var response = await api.PutAsJsonAsync("/api/push/subscription", device.Subscription);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("DEVICE_NOT_FOUND", await response.ErrorCodeAsync());
    }

    public static TheoryData<string?, bool, bool> BadSubscriptions => new()
    {
        { "http://localhost:5432/", true, true },
        { "https://169.254.169.254/latest/meta-data/", true, true },
        { "https://evil.example/push", true, true },
        { "http://fcm.googleapis.com/fcm/send/x", true, true },
        { "not a url", true, true },
        { null, true, true },
        { "https://fcm.googleapis.com/fcm/send/x", false, true }, // p256dh is not a P-256 point
        { "https://fcm.googleapis.com/fcm/send/x", true, false }, // auth is not 16 bytes
    };

    [Theory]
    [MemberData(nameof(BadSubscriptions))]
    public async Task Subscription_OfAnUnknownService_OrWithBadKeys_IsRejected(string? endpoint, bool goodP256dh, bool goodAuth)
    {
        await using var factory = new ApiFactory(Db.ConnectionString, PushOn());
        var alice = await factory.RegisterAsync("alice");
        using var api = factory.CreateClient(alice.Token);
        using var device = new PushTestDevice();

        var response = await api.PutAsJsonAsync("/api/push/subscription", new
        {
            endpoint,
            keys = new
            {
                p256dh = goodP256dh ? device.P256dh : "BAAA",
                auth = goodAuth ? "AAAAAAAAAAAAAAAAAAAAAA" : "AAAA"
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_SUBSCRIPTION", await response.ErrorCodeAsync());
        Assert.All(await SubscriptionsAsync(), s => Assert.Null(s.Endpoint));
    }
}
