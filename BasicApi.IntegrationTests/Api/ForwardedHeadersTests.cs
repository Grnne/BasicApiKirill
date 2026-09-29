using System.Net;
using System.Net.Http.Json;
using BasicApi.IntegrationTests.Infrastructure;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BasicApi.IntegrationTests.Api;

public class ForwardedHeadersTests(PostgresFixture db) : DbTest(db)
{
    private const string ProxyNetwork = "10.9.0.0/24";
    private const string ClientIp = "203.0.113.7";

    private ApiFactory Factory(string remoteIp) => new(
        Db.ConnectionString,
        new Dictionary<string, string?> { ["ReverseProxy:TrustedNetworks"] = ProxyNetwork },
        services: s => s.AddSingleton<IStartupFilter>(new RemoteIpStartupFilter(IPAddress.Parse(remoteIp))));

    private async Task<string?> RegisterAndGetSessionIpAsync(ApiFactory factory, string username)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new { username, email = $"{username}@test.local", password = "secret123" })
        };
        request.Headers.Add("X-Forwarded-For", ClientIp);
        request.Headers.Add("X-Forwarded-Proto", "https");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var connection = new NpgsqlConnection(Db.ConnectionString);
        return await connection.ExecuteScalarAsync<string?>(@"
            SELECT s.ip FROM sessions s JOIN users u ON u.id = s.user_id WHERE u.username = @username",
            new { username });
    }

    [Fact]
    public async Task FromTrustedProxy_ClientIpIsTakenFromXForwardedFor()
    {
        await using var factory = Factory("10.9.0.5");

        Assert.Equal(ClientIp, await RegisterAndGetSessionIpAsync(factory, "via_proxy"));
    }

    [Fact]
    public async Task FromUntrustedAddress_XForwardedForIsIgnored()
    {
        // Иначе любой клиент подделал бы свой IP заголовком и обошёл лимиты по IP.
        await using var factory = Factory("198.51.100.9");

        Assert.Equal("198.51.100.9", await RegisterAndGetSessionIpAsync(factory, "direct"));
    }
}
