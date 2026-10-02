using System.Net;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;

namespace BasicApi.IntegrationTests.Features.Users;

public class UserLookupTests(PostgresFixture db) : DbTest(db)
{
    [Fact]
    public async Task Search_TakesPercentAndUnderscore_AsTheyAre()
    {
        // Found by the review: they went into ILIKE as wildcards, and q=% listed the whole directory.
        await using var factory = new ApiFactory(Db.ConnectionString);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        await factory.RegisterAsync("carol");
        using var client = factory.CreateClient(alice.Token);
        await Data.PrivateChatAsync(alice.UserId, bob.UserId);

        foreach (var q in new[] { "%", "_", "b_b", "%o%" })
        {
            Assert.Empty((await client.GetJsonAsync($"/api/users/search?q={Uri.EscapeDataString(q)}")).GetProperty("items").EnumerateArray());
            Assert.Empty((await client.GetJsonAsync($"/api/chats/search?q={Uri.EscapeDataString(q)}")).GetProperty("items").EnumerateArray());
        }
        Assert.NotEmpty((await client.GetJsonAsync("/api/users/search?q=bo")).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task GetUserId_ByUsername_AnyCase_ReturnsId()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        var alice = await factory.RegisterAsync("Alice", "alice.private@test.local");
        var bob = await factory.RegisterAsync("bob");
        using var client = factory.CreateClient(bob.Token);

        var response = await client.GetAsync("/api/users/GetUserId/aLiCe");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(alice.UserId, body.GetProperty("userId").GetGuid());
    }

    [Fact]
    public async Task GetUserId_ByEmail_IsNotFound()
    {
        // The email is private data: it must not reveal whether a person has an
        // account, nor give away their id.
        await using var factory = new ApiFactory(Db.ConnectionString);
        await factory.RegisterAsync("alice", "alice.private@test.local");
        var bob = await factory.RegisterAsync("bob");
        using var client = factory.CreateClient(bob.Token);

        var response = await client.GetAsync("/api/users/GetUserId/alice.private@test.local");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetUserId_OfDeactivatedUser_IsNotFound()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        await Data.UserAsync("ghost", isActive: false);
        var bob = await factory.RegisterAsync("bob");
        using var client = factory.CreateClient(bob.Token);

        var response = await client.GetAsync("/api/users/GetUserId/ghost");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
