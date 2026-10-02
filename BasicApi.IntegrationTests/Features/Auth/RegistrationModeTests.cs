using System.Net;
using System.Net.Http.Json;
using BasicApi.IntegrationTests.Infrastructure;

namespace BasicApi.IntegrationTests.Features.Auth;

/// <summary>Registration open to anyone, closed, or by invitation of a member.</summary>
public class RegistrationModeTests(PostgresFixture db) : DbTest(db)
{
    private ApiFactory Factory(string mode) =>
        new(Db.ConnectionString, new Dictionary<string, string?>
        {
            ["Registration:Mode"] = mode,
            ["RateLimiting:AuthPerMinute"] = "1000"
        });

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string username, string? inviteCode = null) =>
        client.PostAsJsonAsync("/api/auth/register",
            new { username, email = $"{username}@test.local", password = ApiClient.Password, inviteCode });

    private static async Task<string?> ModeAsync(HttpClient client) =>
        (await client.GetJsonAsync("/api/auth/registration")).GetProperty("mode").GetString();

    [Fact]
    public async Task Open_ByDefault_AnyoneRegisters_AndThereIsNothingToInviteTo()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        using var anonymous = factory.CreateClient();
        Assert.Equal("open", await ModeAsync(anonymous));

        var alice = await factory.RegisterAsync("alice");
        using var aliceApi = factory.CreateClient(alice.Token);
        var invite = await aliceApi.PostAsync("/api/auth/invites", null);
        Assert.Equal(HttpStatusCode.Forbidden, invite.StatusCode);
        Assert.Equal("INVITES_DISABLED", await invite.ErrorCodeAsync());
    }

    [Fact]
    public async Task Closed_NobodyRegisters_TheMembersSignInAsBefore()
    {
        await using (var open = Factory("open"))
            await open.RegisterAsync("alice");
        await using var factory = Factory("closed");
        using var anonymous = factory.CreateClient();

        Assert.Equal("closed", await ModeAsync(anonymous));
        var refused = await RegisterAsync(anonymous, "mallory");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("REGISTRATION_CLOSED", await refused.ErrorCodeAsync());
        await factory.LoginAsync("alice");
    }

    [Fact]
    public async Task Invite_AMemberInvites_OnceAndForAWhile()
    {
        await using (var open = Factory("open"))
        {
            await open.RegisterAsync("alice");
            await open.RegisterAsync("taken");
        }
        await using var factory = Factory("invite");
        using var anonymous = factory.CreateClient();
        using var aliceApi = factory.CreateClient((await factory.LoginAsync("alice")).Token);
        Assert.Equal("invite", await ModeAsync(anonymous));

        var invite = await (await aliceApi.PostAsync("/api/auth/invites", null)).ReadJsonAsync();
        var code = invite.GetProperty("code").GetString()!;
        Assert.True(invite.GetProperty("expiresAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddDays(6));

        foreach (var wrong in new string?[] { null, "", "not-a-code" })
            Assert.Equal("INVITE_INVALID", await (await RegisterAsync(anonymous, "bob", wrong)).ErrorCodeAsync());
        // A registration that fails for its own reasons does not use the invite up.
        Assert.Equal(HttpStatusCode.Conflict, (await RegisterAsync(anonymous, "taken", code)).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await RegisterAsync(anonymous, "bob", code)).StatusCode);
        Assert.Equal("INVITE_INVALID", await (await RegisterAsync(anonymous, "carol", code)).ErrorCodeAsync());

        var stale = (await (await aliceApi.PostAsync("/api/auth/invites", null)).ReadJsonAsync()).GetProperty("code").GetString();
        await NewSession().ExecuteAsync("UPDATE invites SET expires_at = now() - interval '1 minute'");
        Assert.Equal("INVITE_INVALID", await (await RegisterAsync(anonymous, "dave", stale)).ErrorCodeAsync());
    }
}
