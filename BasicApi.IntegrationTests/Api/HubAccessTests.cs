using System.Net;
using System.Net.Http.Json;
using BasicApi.IntegrationTests.Infrastructure;
using Dapper;
using Microsoft.AspNetCore.SignalR.Client;
using Npgsql;

namespace BasicApi.IntegrationTests.Api;

/// <summary>
/// Соединение с хабом живёт часами, а access-токен — минуты. Соединение живёт, пока
/// жив вход (сессия), с которого оно открыто: отозванный или истёкший вход обрывает
/// и уже открытые соединения, а истечение одного access-токена — нет.
/// </summary>
public class HubAccessTests(PostgresFixture db) : DbTest(db)
{
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Проверка сессий открытых соединений — раз в секунду, а не в минуту.</summary>
    private static readonly Dictionary<string, string?> FastSessionCheck = new()
    {
        ["Hub:SessionCheckIntervalSeconds"] = "1",
    };

    [Fact]
    public async Task Connection_OutlivesAccessToken_WhileSessionIsLive()
    {
        // Текущий веб-клиент при переподключении отдаёт тот же токен, пока не получит
        // 401 на REST-запросе. Закрытие соединения по истечении токена оставляло его
        // без событий до перезагрузки страницы (найдено ручной проверкой клиента).
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
        // Токен без sid не привязан ко входу — нечем проверить, что доступ ещё есть.
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
        // Повтор уже использованного refresh-токена гасит всю цепочку входа (кража).
        // Соединение, открытое с этого входа, должно закрыться, хотя access-токен жив.
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
