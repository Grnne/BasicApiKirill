using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.SignalR.Client;
using static BasicApi.IntegrationTests.Features.Media.MediaUploadTests;

namespace BasicApi.IntegrationTests.Features.Users;

/// <summary>Blocks (plan 2, F5.3, D11).</summary>
public class BlocksTests(PostgresFixture db, StorageFixture storage) : DbTest(db)
{
    private ApiFactory Factory() => new(Db.ConnectionString, storage.Settings());

    private static async Task<string?> CodeAsync(Task<HttpResponseMessage> call) => await (await call).ErrorCodeAsync();

    [Fact]
    public async Task Block_StopsThePrivateChat_BothWays_UntilUnblocked()
    {
        await using var factory = Factory();
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        var hello = (await aliceApi.PostJsonAsync($"/api/chats/{chat}/messages", new { text = "hello" })).Id();

        Assert.Equal(HttpStatusCode.NoContent, (await aliceApi.PutAsync($"/api/users/{bob.UserId}/block", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await aliceApi.PutAsync($"/api/users/{bob.UserId}/block", null)).StatusCode);

        // Bob is not told it is a block; Alice is told to unblock.
        Assert.Equal("PRIVACY_RESTRICTED", await CodeAsync(bobApi.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text = "hey?" })));
        Assert.Equal("PRIVACY_RESTRICTED", await CodeAsync(bobApi.PutAsJsonAsync($"/api/chats/{chat}/messages/{hello}/reactions", new { emoji = "👍" })));
        Assert.Equal("PRIVACY_RESTRICTED", await CodeAsync(bobApi.PostAsJsonAsync($"/api/chats/{chat}/typing", new { isTyping = true })));
        Assert.Equal("USER_BLOCKED", await CodeAsync(aliceApi.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text = "bye" })));
        // Reading still works: the history is theirs.
        Assert.Single(await bobApi.HistoryAsync(chat));

        var blocked = await aliceApi.GetJsonAsync("/api/users/me/blocked");
        Assert.Equal(bob.UserId, Assert.Single(blocked.EnumerateArray()).Id("userId"));
        var change = Assert.Single(await aliceApi.JournalAsync("BlockListChanged"));
        Assert.True(change.GetProperty("blocked").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await aliceApi.DeleteAsync($"/api/users/{bob.UserId}/block")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await bobApi.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text = "hey?" })).StatusCode);
        Assert.Empty((await aliceApi.GetJsonAsync("/api/users/me/blocked")).EnumerateArray());

        Assert.Equal("INVALID_REQUEST", await CodeAsync(aliceApi.PutAsync($"/api/users/{alice.UserId}/block", null)));
        Assert.Equal("USER_NOT_FOUND", await CodeAsync(aliceApi.PutAsync($"/api/users/{Guid.NewGuid()}/block", null)));
    }

    [Fact]
    public async Task Blocked_NeitherStartsAChat_NorAddsToGroups()
    {
        await using var factory = Factory();
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        await aliceApi.PutAsync($"/api/users/{bob.UserId}/block", null);

        Assert.Equal("PRIVACY_RESTRICTED", await CodeAsync(bobApi.PostAsync($"/api/chats/private/{alice.UserId}", null)));
        var group = await bobApi.PostAsJsonAsync("/api/chats/groups", new { title = "g", memberIds = new[] { alice.UserId } });
        Assert.Equal("PRIVACY_RESTRICTED", await group.ErrorCodeAsync());
        Assert.Equal(alice.UserId, (await group.ReadJsonAsync()).GetProperty("userIds")[0].GetGuid());
    }

    [Fact]
    public async Task Blocked_SeesNeitherPresenceNorAvatar()
    {
        await using var factory = Factory();
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carl = await factory.RegisterAsync("carl");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        using var carlApi = factory.CreateClient(carl.Token);
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        await Data.PrivateChatAsync(alice.UserId, carl.UserId);
        var photo = (await UploadAsync(aliceApi, "photo", Png(16, 16), "me.png", "image/png")).Id();
        await aliceApi.PutAsJsonAsync("/api/users/me/avatar", new { attachmentId = photo });

        var seen = new ConcurrentQueue<(Guid, bool)>();
        await using var aliceHub = factory.CreateHubConnection(alice.Token);
        await using var bobHub = factory.CreateHubConnection(bob.Token);
        bobHub.On<Guid, bool>("UserOnlineChanged", (id, online) => seen.Enqueue((id, online)));
        await bobHub.StartAsync();
        await aliceHub.StartAsync();
        await WaitAsync(() => seen.Contains((alice.UserId, true)));

        await aliceApi.PutAsync($"/api/users/{bob.UserId}/block", null);

        await WaitAsync(() => seen.Contains((alice.UserId, false)));
        var status = await bobApi.GetJsonAsync($"/api/users/{alice.UserId}/status");
        Assert.False(status.GetProperty("isOnline").GetBoolean());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("lastSeenAt").ValueKind);

        Assert.Equal(JsonValueKind.Null, (await bobApi.GetJsonAsync($"/api/users/{alice.UserId}")).GetProperty("avatarId").ValueKind);
        Assert.Equal(JsonValueKind.Null, (await bobApi.ChatItemAsync(chat)).GetProperty("avatarId").ValueKind);
        Assert.Equal(JsonValueKind.Null, (await bobApi.GetJsonAsync($"/api/chats/{chat}")).GetProperty("avatarId").ValueKind);
        Assert.Empty((await bobApi.PostJsonAsync("/api/media/links", new { attachmentIds = new[] { photo } })).GetProperty("items").EnumerateArray());
        // Others still see it.
        Assert.Equal(photo, (await carlApi.GetJsonAsync($"/api/users/{alice.UserId}")).Id("avatarId"));
        Assert.Single((await carlApi.PostJsonAsync("/api/media/links", new { attachmentIds = new[] { photo } })).GetProperty("items").EnumerateArray());

        // Unblocked: Alice is back online for Bob.
        seen.Clear();
        await aliceApi.DeleteAsync($"/api/users/{bob.UserId}/block");
        await WaitAsync(() => seen.Contains((alice.UserId, true)));
        Assert.Equal(photo, (await bobApi.ChatItemAsync(chat)).Id("avatarId"));
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(50);
        Assert.True(condition());
    }
}
