using System.Net;
using System.Net.Http.Json;
using BasicApi.IntegrationTests.Infrastructure;
using Dapper;
using Npgsql;

namespace BasicApi.IntegrationTests.Api;

/// <summary>The name shown to others and the password (plan 2, F5.4).</summary>
public class ProfileTests(PostgresFixture db) : DbTest(db)
{
    private static Dictionary<string, string?> Settings => new() { ["RateLimiting:CommandsPer10Seconds"] = "1000" };

    [Fact]
    public async Task DisplayName_Changes_ForEveryoneAndEverywhere()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        await aliceApi.PostJsonAsync($"/api/chats/{chat}/messages", new { text = "hi" });

        var profile = await aliceApi.PatchAsJsonAsync("/api/users/me", new { displayName = "  Alice Liddell  " });
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        Assert.Equal("Alice Liddell", (await profile.ReadJsonAsync()).GetProperty("displayName").GetString());

        var update = Assert.Single(await bobApi.JournalAsync("UserUpdated"));
        Assert.Equal("Alice Liddell", update.GetProperty("displayName").GetString());
        Assert.Equal("Alice Liddell", (await bobApi.ChatItemAsync(chat)).GetProperty("companionName").GetString());
        // Sent messages show the name as it is now.
        Assert.Equal("Alice Liddell", (await bobApi.HistoryAsync(chat))[0].GetProperty("senderName").GetString());

        Assert.Equal("INVALID_DISPLAY_NAME", await (await aliceApi.PatchAsJsonAsync("/api/users/me", new { displayName = "   " })).ErrorCodeAsync());
        Assert.Equal("INVALID_DISPLAY_NAME", await (await aliceApi.PatchAsJsonAsync("/api/users/me", new { displayName = new string('a', 101) })).ErrorCodeAsync());
        // Nothing to change: nothing is announced.
        await aliceApi.PatchAsJsonAsync("/api/users/me", new { displayName = "Alice Liddell" });
        Assert.Single(await bobApi.JournalAsync("UserUpdated"));
    }

    [Fact]
    public async Task PasswordChange_KeepsThisDevice_AndSignsOutTheOthersAtOnce()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var laptop = await factory.RegisterAsync("alice");
        var phone = await factory.LoginAsync("alice");
        using var laptopApi = factory.CreateClient(laptop.Token);
        await using var phoneHub = factory.CreateHubConnection(phone.Token);
        await phoneHub.StartAsync();

        var wrong = await laptopApi.PostAsJsonAsync("/api/auth/password",
            new { currentPassword = "not-it", newPassword = "brand-new-secret" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("WRONG_PASSWORD", await wrong.ErrorCodeAsync());

        var changed = await laptopApi.PostAsJsonAsync("/api/auth/password",
            new { currentPassword = ApiClient.Password, newPassword = "brand-new-secret" });
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        Assert.True(await phoneHub.WaitForCloseAsync(TimeSpan.FromSeconds(10)));
        using var client = factory.CreateClient();
        var phoneRefresh = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = phone.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, phoneRefresh.StatusCode);
        var laptopRefresh = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = laptop.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, laptopRefresh.StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/login", new { usernameOrEmail = "alice", password = "brand-new-secret" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await using var connection = new NpgsqlConnection(Db.ConnectionString);
        var hash = await connection.ExecuteScalarAsync<string>("SELECT password_hash FROM users WHERE id = @id", new { id = laptop.UserId });
        Assert.False(BCrypt.Net.BCrypt.Verify(ApiClient.Password, hash));
    }
}
