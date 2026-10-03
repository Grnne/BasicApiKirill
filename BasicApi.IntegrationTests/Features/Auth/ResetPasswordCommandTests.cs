using System.Net;
using System.Net.Http.Json;
using BasicApi.Features.Auth;
using BasicApi.IntegrationTests.Infrastructure;

namespace BasicApi.IntegrationTests.Features.Auth;

/// <summary>The administrator's <c>--reset-password</c>, against the real database.</summary>
public class ResetPasswordCommandTests(PostgresFixture db) : DbTest(db)
{
    private static async Task<(int Code, string Output, string Errors)> RunAsync(ApiFactory factory, params string[] args)
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();
        var code = await ResetPasswordCommand.RunAsync(factory.Services, args, output, errors);
        return (code, output.ToString(), errors.ToString());
    }

    private static Task<HttpResponseMessage> LoginAsync(ApiFactory factory, string login, string password) =>
        factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { usernameOrEmail = login, password });

    [Fact]
    public async Task Reset_GivesATemporaryPassword_AndEndsEverySignIn()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");

        var (code, output, _) = await RunAsync(factory, ResetPasswordCommand.Flag, " Alice ");

        Assert.Equal(0, code);
        var password = output.Split('\n')[0].Split(": ")[1].Trim();
        Assert.True(password.Length >= 16);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(factory, "alice", ApiClient.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(factory, "alice", password)).StatusCode);
        // Whoever was signed in with the old password is out: the refresh token and the access token.
        var refresh = await factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = alice.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient(alice.Token).GetAsync("/api/chats")).StatusCode);
        // Nobody else is touched.
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient(bob.Token).GetAsync("/api/chats")).StatusCode);
    }

    [Fact]
    public async Task UnknownLogin_OrNone_IsAnErrorAndChangesNothing()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        await factory.RegisterAsync("alice");

        Assert.Equal(1, (await RunAsync(factory, ResetPasswordCommand.Flag, "nobody")).Code);
        Assert.Equal(2, (await RunAsync(factory, ResetPasswordCommand.Flag)).Code);
        Assert.Equal(2, (await RunAsync(factory, ResetPasswordCommand.Flag, "--other")).Code);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(factory, "alice", ApiClient.Password)).StatusCode);
    }
}
