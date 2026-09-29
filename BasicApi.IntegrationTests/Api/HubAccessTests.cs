using System.Net.Http.Json;
using BasicApi.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.SignalR.Client;

namespace BasicApi.IntegrationTests.Api;

/// <summary>
/// Соединение с хабом живёт часами, а токен — минуты. Доступ, отозванный
/// или истёкший, должен обрывать и уже открытые соединения.
/// </summary>
public class HubAccessTests(PostgresFixture db) : DbTest(db)
{
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task Connection_IsClosed_WhenAccessTokenExpires()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        var user = await factory.RegisterAsync("alice");
        await using var hub = factory.CreateHubConnection(
            ApiClient.ShortLivedToken(user.UserId, TimeSpan.FromSeconds(3)));
        await hub.StartAsync();

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
        // Access-токен ещё не истёк, но сессия уже закрыта: переподключиться нельзя,
        // иначе logout-all обходится простым реконнектом.
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
            // отказ прямо на старте — тоже правильный исход
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
