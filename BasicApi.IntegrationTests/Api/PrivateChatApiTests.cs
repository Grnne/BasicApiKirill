using System.Net;
using BasicApi.IntegrationTests.Infrastructure;

namespace BasicApi.IntegrationTests.Api;

public class PrivateChatApiTests(PostgresFixture db) : DbTest(db)
{
    [Fact]
    public async Task CreatePrivateChat_WithUnknownUser_Returns404()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        var alice = await factory.RegisterAsync("alice");
        using var client = factory.CreateClient(alice.Token);

        var response = await client.PostAsync($"/api/chats/private/{Guid.NewGuid()}", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("USER_NOT_FOUND", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CreatePrivateChat_Twice_ReturnsCreatedThenOk_WithSameChat()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var client = factory.CreateClient(alice.Token);

        var first = await client.PostAsync($"/api/chats/private/{bob.UserId}", null);
        var second = await client.PostAsync($"/api/chats/private/{bob.UserId}", null);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstId = System.Text.Json.JsonDocument.Parse(await first.Content.ReadAsStringAsync())
            .RootElement.GetProperty("chatId").GetGuid();
        var secondId = System.Text.Json.JsonDocument.Parse(await second.Content.ReadAsStringAsync())
            .RootElement.GetProperty("chatId").GetGuid();
        Assert.Equal(firstId, secondId);
    }
}
