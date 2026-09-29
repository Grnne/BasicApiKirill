using System.Net;
using System.Net.Http.Headers;
using BasicApi.IntegrationTests.Infrastructure;
using BasicApi.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace BasicApi.IntegrationTests.Api;

[Collection(PostgresCollection.Name)]
public class RateLimitingTests(PostgresFixture db)
{
    // All requests come from one IP, as from an office behind NAT.
    private ApiFactory Factory() => new(
        db.ConnectionString,
        new Dictionary<string, string?>
        {
            ["RateLimiting:PerUserPerMinute"] = "3",
            ["RateLimiting:PerIpPerMinute"] = "2"
        },
        services: s => s.AddSingleton<IStartupFilter>(new RemoteIpStartupFilter(IPAddress.Parse("198.51.100.1"))));

    private static async Task<HttpStatusCode> GetChatsAsync(HttpClient client, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/chats");
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await client.SendAsync(request)).StatusCode;
    }

    private static string Token(ApiFactory factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IJwtService>()
            .GenerateToken(userId, $"u{userId:N}", $"{userId:N}@test.local");
    }

    [Fact]
    public async Task AuthenticatedUsers_BehindOneIp_HaveSeparateBudgets()
    {
        await using var factory = Factory();
        using var client = factory.CreateClient();
        var alice = Token(factory, Guid.NewGuid());
        var bob = Token(factory, Guid.NewGuid());

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, await GetChatsAsync(client, alice));
        Assert.Equal(HttpStatusCode.TooManyRequests, await GetChatsAsync(client, alice));

        // Alice's limit is exhausted, but Bob from the same IP is unaffected.
        Assert.Equal(HttpStatusCode.OK, await GetChatsAsync(client, bob));
    }

    [Fact]
    public async Task AnonymousRequests_AreLimitedByIp_Separately_FromUsers()
    {
        await using var factory = Factory();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, await GetChatsAsync(client, null));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetChatsAsync(client, null));
        Assert.Equal(HttpStatusCode.TooManyRequests, await GetChatsAsync(client, null));

        // The anonymous IP budget is exhausted - an authorized user from the same IP still works.
        Assert.Equal(HttpStatusCode.OK, await GetChatsAsync(client, Token(factory, Guid.NewGuid())));
    }
}
