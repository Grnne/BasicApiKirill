using System.Net;
using System.Net.Http.Json;
using BasicApi.IntegrationTests.Infrastructure;
using Dapper;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BasicApi.IntegrationTests.Platform;

/// <summary>
/// A hub connection lives as long as the sign-in it was opened from: a revoked or expired sign-in
/// drops open connections, the expiry of a single access token does not.
/// </summary>
public class HubAccessTests(PostgresFixture db) : DbTest(db)
{
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Sessions of open connections are checked once a second, not once a minute.</summary>
    private static readonly Dictionary<string, string?> FastSessionCheck = new()
    {
        ["Hub:SessionCheckIntervalSeconds"] = "1",
    };

    [Fact]
    public async Task ConnectionDroppedWhileConnecting_DoesNotLeaveTheUserOnline()
    {
        // A client that leaves while the hub is still connecting it (a quick reload, a flaky
        // network) aborted OnConnectedAsync after the user was marked online; SignalR then does not
        // call OnDisconnectedAsync, and the user stayed online until a restart.
        await using var factory = new ApiFactory(Db.ConnectionString);
        var alice = await factory.RegisterAsync("alice");
        var status = factory.Services.GetRequiredService<BasicApi.Services.IUserStatusService>();

        for (var i = 0; i < 10; i++)
        {
            await using var hub = factory.CreateHubConnection(alice.Token);
            await hub.StartAsync();
            await hub.StopAsync();
        }

        var connections = -1;
        for (var i = 0; i < 100 && connections != 0; i++)
        {
            await Task.Delay(50);
            connections = await status.GetConnectionCountAsync(alice.UserId);
        }
        Assert.Equal(0, connections);
    }

    [Fact]
    public async Task Connection_OutlivesAccessToken_WhileSessionIsLive()
    {
        // On reconnect the current web client sends the same token until it gets
        // 401 on a REST request. Closing the connection on token expiry left it
        // without events until the page was reloaded.
        await using var factory = new ApiFactory(Db.ConnectionString, FastSessionCheck);
        var user = await factory.RegisterAsync("alice");
        await using var hub = factory.CreateHubConnection(ApiClient.ShortLivedToken(
            user.UserId, TimeSpan.FromSeconds(2), ApiClient.SessionFamilyOf(user.Token)));
        await hub.StartAsync();

        Assert.False(await hub.WaitForCloseAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(HubConnectionState.Connected, hub.State);
    }

    [Fact]
    public async Task Connection_WithoutSession_IsClosed_WhenAccessTokenExpires()
    {
        // A token without sid is not bound to a sign-in - there is no way to check that access still exists.
        await using var factory = new ApiFactory(Db.ConnectionString, FastSessionCheck);
        var user = await factory.RegisterAsync("alice");
        await using var hub = factory.CreateHubConnection(
            ApiClient.ShortLivedToken(user.UserId, TimeSpan.FromSeconds(3)));
        await hub.StartAsync();

        Assert.True(await hub.WaitForCloseAsync(CloseTimeout));
    }

    [Fact]
    public async Task Connection_IsClosed_WhenRefreshTokenReuseRevokesTheSession()
    {
        // Reuse of an already used refresh token kills the whole sign-in chain (theft).
        // The connection opened from this sign-in must close even though the access token is still alive.
        await using var factory = new ApiFactory(Db.ConnectionString, new Dictionary<string, string?>(FastSessionCheck)
        {
            ["Jwt:RefreshGraceSeconds"] = "0",
        });
        var user = await factory.RegisterAsync("alice");
        await using var hub = factory.CreateHubConnection(user.Token);
        await hub.StartAsync();

        using var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = user.RefreshToken }))
            .EnsureSuccessStatusCode();
        await Task.Delay(TimeSpan.FromSeconds(1.1));
        var reuse = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = user.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        Assert.True(await hub.WaitForCloseAsync(CloseTimeout));
    }

    [Fact]
    public async Task Connection_IsClosed_WhenSessionExpires()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, FastSessionCheck);
        var user = await factory.RegisterAsync("alice");
        await using var hub = factory.CreateHubConnection(user.Token);
        await hub.StartAsync();

        await using (var connection = new NpgsqlConnection(Db.ConnectionString))
            await connection.ExecuteAsync("UPDATE sessions SET expires_at = now() - interval '1 second' WHERE family_id = @id",
                new { id = ApiClient.SessionFamilyOf(user.Token) });

        Assert.True(await hub.WaitForCloseAsync(CloseTimeout));
    }

    [Fact]
    public async Task LogoutAll_ClosesEveryConnectionOfTheUser()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        var phone = await factory.RegisterAsync("alice");
        var laptop = await factory.LoginAsync("alice");
        await using var phoneHub = factory.CreateHubConnection(phone.Token);
        await using var laptopHub = factory.CreateHubConnection(laptop.Token);
        await phoneHub.StartAsync();
        await laptopHub.StartAsync();

        using var client = factory.CreateClient(phone.Token);
        (await client.PostAsync("/api/auth/logout-all", null)).EnsureSuccessStatusCode();

        Assert.True(await phoneHub.WaitForCloseAsync(CloseTimeout));
        Assert.True(await laptopHub.WaitForCloseAsync(CloseTimeout));
    }

    [Fact]
    public async Task Logout_ClosesOnlyThatSessionsConnections()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        var phone = await factory.RegisterAsync("alice");
        var laptop = await factory.LoginAsync("alice");
        await using var phoneHub = factory.CreateHubConnection(phone.Token);
        await using var laptopHub = factory.CreateHubConnection(laptop.Token);
        await phoneHub.StartAsync();
        await laptopHub.StartAsync();

        using var client = factory.CreateClient(phone.Token);
        (await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = phone.RefreshToken }))
            .EnsureSuccessStatusCode();

        Assert.True(await phoneHub.WaitForCloseAsync(CloseTimeout));
        Assert.Equal(HubConnectionState.Connected, laptopHub.State);
    }

    [Fact]
    public async Task Reconnect_WithTokenOfRevokedSession_IsRejected()
    {
        // The access token has not expired yet, but the session is already closed: reconnecting is impossible,
        // otherwise logout-all would be bypassed by a simple reconnect.
        await using var factory = new ApiFactory(Db.ConnectionString);
        var user = await factory.RegisterAsync("alice");
        using var client = factory.CreateClient(user.Token);
        (await client.PostAsync("/api/auth/logout-all", null)).EnsureSuccessStatusCode();

        await using var hub = factory.CreateHubConnection(user.Token);
        try
        {
            await hub.StartAsync();
        }
        catch (Exception)
        {
            // a refusal right at startup is also a correct outcome
        }

        Assert.True(await hub.WaitForCloseAsync(CloseTimeout));
    }

    [Fact]
    public async Task LiveSession_StaysConnected()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        var user = await factory.RegisterAsync("alice");
        await using var hub = factory.CreateHubConnection(user.Token);
        await hub.StartAsync();

        Assert.False(await hub.WaitForCloseAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(HubConnectionState.Connected, hub.State);
    }
}
