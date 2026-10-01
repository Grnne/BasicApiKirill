using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;
using BasicApi.Services.Events;
using Dapper;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BasicApi.IntegrationTests.Features.Devices;

/// <summary>Devices: every sign-in is one, until it ends.</summary>
public class DevicesTests(PostgresFixture db) : DbTest(db)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static async Task<AuthResult> LoginFromAsync(ApiFactory factory, string username, string userAgent)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        var response = await client.PostAsJsonAsync("/api/auth/login", new { usernameOrEmail = username, password = ApiClient.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthResult>(Json))!;
    }

    private static async Task<List<JsonElement>> DevicesAsync(HttpClient api) =>
        [.. (await api.GetJsonAsync("/api/devices")).GetProperty("items").EnumerateArray()];

    [Fact]
    public async Task List_ShowsOpenSignIns_WithTheCurrentOneMarked()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        var first = await factory.RegisterAsync("alice");
        var phone = await LoginFromAsync(factory, "alice", "Phone/1.0");
        var laptop = await LoginFromAsync(factory, "alice", "Laptop/2.0");
        await factory.RegisterAsync("bob");
        using var api = factory.CreateClient(laptop.Token);

        var devices = await DevicesAsync(api);

        Assert.Equal(3, devices.Count);
        Assert.Equal(
            new[] { first.Token, phone.Token, laptop.Token }.Select(ApiClient.SessionFamilyOf).Order(),
            devices.Select(d => d.Id()).Order());
        var current = Assert.Single(devices, d => d.GetProperty("isCurrent").GetBoolean());
        Assert.Equal(ApiClient.SessionFamilyOf(laptop.Token), current.Id());
        Assert.Equal("Laptop/2.0", current.GetProperty("userAgent").GetString());
        // The most recently active first.
        Assert.Equal(current.Id(), devices[0].Id());

        // Logout takes the device off the list.
        using var phoneApi = factory.CreateClient(phone.Token);
        (await phoneApi.PostAsJsonAsync("/api/auth/logout", new { refreshToken = phone.RefreshToken })).EnsureSuccessStatusCode();
        Assert.DoesNotContain(ApiClient.SessionFamilyOf(phone.Token), (await DevicesAsync(api)).Select(d => d.Id()));
    }

    [Fact]
    public async Task Refresh_KeepsTheDevice_AndMovesItsLastActivity()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        var alice = await factory.RegisterAsync("alice");
        using var api = factory.CreateClient(alice.Token);
        var before = Assert.Single(await DevicesAsync(api));

        using var anonymous = factory.CreateClient();
        anonymous.DefaultRequestHeaders.UserAgent.ParseAdd("Renamed/3.0");
        (await anonymous.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = alice.RefreshToken })).EnsureSuccessStatusCode();

        var after = Assert.Single(await DevicesAsync(api));
        Assert.Equal(before.Id(), after.Id());
        Assert.Equal(before.GetProperty("signedInAt").GetDateTime(), after.GetProperty("signedInAt").GetDateTime());
        Assert.True(after.GetProperty("lastActiveAt").GetDateTime() >= before.GetProperty("lastActiveAt").GetDateTime());
        Assert.Equal("Renamed/3.0", after.GetProperty("userAgent").GetString());
    }

    [Fact]
    public async Task SignOut_EndsThatSignIn_AndClosesItsConnections()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        var laptop = await factory.RegisterAsync("alice");
        var phone = await factory.LoginAsync("alice");
        await using var phoneHub = factory.CreateHubConnection(phone.Token);
        await using var laptopHub = factory.CreateHubConnection(laptop.Token);
        await phoneHub.StartAsync();
        await laptopHub.StartAsync();
        using var api = factory.CreateClient(laptop.Token);
        var phoneId = ApiClient.SessionFamilyOf(phone.Token);

        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteAsync($"/api/devices/{phoneId}")).StatusCode);

        Assert.True(await phoneHub.WaitForCloseAsync(TimeSpan.FromSeconds(15)));
        Assert.Equal(HubConnectionState.Connected, laptopHub.State);
        using var anonymous = factory.CreateClient();
        var refresh = await anonymous.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = phone.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.Equal([ApiClient.SessionFamilyOf(laptop.Token)], (await DevicesAsync(api)).Select(d => d.Id()));

        // Already signed out.
        var again = await api.DeleteAsync($"/api/devices/{phoneId}");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal("DEVICE_NOT_FOUND", await again.ErrorCodeAsync());
    }

    [Fact]
    public async Task SignOut_OfSomeoneElsesDevice_IsNotFound()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var bobApi = factory.CreateClient(bob.Token);

        var response = await bobApi.DeleteAsync($"/api/devices/{ApiClient.SessionFamilyOf(alice.Token)}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = alice.RefreshToken })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task LogoutAll_AndPasswordChange_RemoveTheOtherDevices()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        var alice = await factory.RegisterAsync("alice");
        await factory.LoginAsync("alice");
        await factory.LoginAsync("alice");
        using var api = factory.CreateClient(alice.Token);

        (await api.PostAsJsonAsync("/api/auth/password", new { currentPassword = ApiClient.Password, newPassword = "another123" }))
            .EnsureSuccessStatusCode();
        Assert.Equal([ApiClient.SessionFamilyOf(alice.Token)], (await DevicesAsync(api)).Select(d => d.Id()));

        (await api.PostAsync("/api/auth/logout-all", null)).EnsureSuccessStatusCode();
        Assert.Empty(await DevicesAsync(api));
        await using var connection = new NpgsqlConnection(Db.ConnectionString);
        Assert.Equal(0, await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM devices"));
    }

    [Fact]
    public async Task Cleanup_RemovesDevicesWhoseSignInEnded()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        var alice = await factory.RegisterAsync("alice");
        var expired = await factory.LoginAsync("alice");
        var stolen = await factory.LoginAsync("alice");
        await using var connection = new NpgsqlConnection(Db.ConnectionString);
        await connection.ExecuteAsync("UPDATE sessions SET expires_at = now() - interval '1 minute' WHERE family_id = @id",
            new { id = ApiClient.SessionFamilyOf(expired.Token) });
        // Token theft revokes the chain in the session store only.
        await connection.ExecuteAsync("UPDATE sessions SET revoked_at = now() WHERE family_id = @id",
            new { id = ApiClient.SessionFamilyOf(stolen.Token) });

        var (_, _, devices) = await factory.Services.GetRequiredService<JournalCleanup>().CleanupAsync();

        Assert.Equal(2, devices);
        Assert.Equal([ApiClient.SessionFamilyOf(alice.Token)],
            await connection.QueryAsync<Guid>("SELECT id FROM devices"));
    }
}
