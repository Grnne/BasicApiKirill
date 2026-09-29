using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;

namespace BasicApi.IntegrationTests.Api;

/// <summary>The group's action log (plan 2, F3.5).</summary>
public class GroupAuditTests(PostgresFixture db) : DbTest(db)
{
    private static Dictionary<string, string?> Settings => new() { ["RateLimiting:CommandsPer10Seconds"] = "1000" };

    [Fact]
    public async Task Admins_SeeWhatWasDoneInTheGroup_NewestFirst_PageByPage()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carol = await factory.RegisterAsync("carol");
        var dave = await factory.RegisterAsync("dave");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        using var carolApi = factory.CreateClient(carol.Token);
        var chatId = (await aliceApi.PostJsonAsync("/api/chats/groups",
            new { title = "team", memberIds = new[] { bob.UserId, carol.UserId } })).Id("chatId");

        (await aliceApi.PatchAsJsonAsync($"/api/chats/{chatId}", new { title = "Launch" })).EnsureSuccessStatusCode();
        (await aliceApi.PutAsJsonAsync($"/api/chats/{chatId}/members/{bob.UserId}/role", new { role = "admin" })).EnsureSuccessStatusCode();
        (await bobApi.PostAsJsonAsync($"/api/chats/{chatId}/members", new { userIds = new[] { dave.UserId } })).EnsureSuccessStatusCode();
        (await bobApi.PutAsJsonAsync($"/api/chats/{chatId}/members/{dave.UserId}/permissions", new { sendMedia = false }))
            .EnsureSuccessStatusCode();
        (await bobApi.DeleteAsync($"/api/chats/{chatId}/members/{dave.UserId}")).EnsureSuccessStatusCode();
        (await carolApi.DeleteAsync($"/api/chats/{chatId}/members/{carol.UserId}")).EnsureSuccessStatusCode();

        var entries = new List<JsonElement>();
        string? cursor = null;
        do
        {
            var page = await bobApi.GetJsonAsync($"/api/chats/{chatId}/audit?limit=3{(cursor is null ? "" : $"&cursor={cursor}")}");
            entries.AddRange(page.GetProperty("items").EnumerateArray());
            cursor = page.GetProperty("nextCursor").GetString();
            Assert.Equal(cursor is not null, page.GetProperty("hasMore").GetBoolean());
        } while (cursor is not null);

        Assert.Equal(
            ["member_left", "member_removed", "permissions_changed", "members_added", "role_changed", "title_changed", "group_created"],
            entries.Select(e => e.GetProperty("action").GetString()));
        var removal = entries[1];
        Assert.Equal(bob.UserId, removal.Id("actorId"));
        Assert.Equal(dave.UserId, removal.Id("targetUserId"));
        Assert.Equal("Launch", entries[5].GetProperty("data").GetProperty("to").GetString());
        Assert.Equal("admin", entries[4].GetProperty("data").GetProperty("role").GetString());
        Assert.False(entries[2].GetProperty("data").GetProperty("permissions").GetProperty("sendMedia").GetBoolean());

        // Members do not see it; a broken cursor is refused.
        using var daveApi = factory.CreateClient(dave.Token);
        var stranger = await daveApi.GetAsync($"/api/chats/{chatId}/audit");
        Assert.Equal("NOT_A_MEMBER", await stranger.ErrorCodeAsync());
        (await aliceApi.PostAsJsonAsync($"/api/chats/{chatId}/members", new { userIds = new[] { dave.UserId } })).EnsureSuccessStatusCode();
        Assert.Equal("PERMISSION_DENIED", await (await daveApi.GetAsync($"/api/chats/{chatId}/audit")).ErrorCodeAsync());
        var broken = await aliceApi.GetAsync($"/api/chats/{chatId}/audit?cursor=abc");
        Assert.Equal(HttpStatusCode.BadRequest, broken.StatusCode);
        Assert.Equal("INVALID_CURSOR", await broken.ErrorCodeAsync());
    }
}
