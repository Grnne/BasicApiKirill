using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.SignalR.Client;

namespace BasicApi.IntegrationTests.Features.Chats;

public class PresenceOnChatCreationTests(PostgresFixture db) : DbTest(db)
{
    [Fact]
    public async Task NewPrivateChat_BetweenOnlineUsers_BothSeeEachOtherOnline()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");

        var aliceSees = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bobSees = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var aliceHub = factory.CreateHubConnection(alice.Token);
        await using var bobHub = factory.CreateHubConnection(bob.Token);
        aliceHub.On<Guid, bool>("UserOnlineChanged", (id, online) => { if (online) aliceSees.TrySetResult(id); });
        bobHub.On<Guid, bool>("UserOnlineChanged", (id, online) => { if (online) bobSees.TrySetResult(id); });
        await aliceHub.StartAsync();
        await bobHub.StartAsync();

        using var client = factory.CreateClient(alice.Token);
        (await client.PostAsync($"/api/chats/private/{bob.UserId}", null)).EnsureSuccessStatusCode();

        Assert.Equal(bob.UserId, await aliceSees.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(alice.UserId, await bobSees.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task NewPrivateChat_WithSomeoneWhoHidesBeingOnline_DoesNotShowThem()
    {
        // Found by the review: getting acquainted in a new chat announced "online" to the other
        // side without the privacy check every other presence path makes.
        await using var factory = new ApiFactory(Db.ConnectionString);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        (await bobApi.PutAsJsonAsync("/api/users/me/privacy",
            new { lastSeen = "nobody", messages = "everybody", groupAdd = "everybody" })).EnsureSuccessStatusCode();

        var aliceSaw = new ConcurrentBag<Guid>();
        var aliceGotMessage = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var aliceHub = factory.CreateHubConnection(alice.Token);
        await using var bobHub = factory.CreateHubConnection(bob.Token);
        aliceHub.On<Guid, bool>("UserOnlineChanged", (id, online) => { if (online) aliceSaw.Add(id); });
        aliceHub.On<Guid, JsonElement>("ChatListUpdated", (_, _) => aliceGotMessage.TrySetResult());
        await aliceHub.StartAsync();
        await bobHub.StartAsync();

        var chat = await aliceApi.PostJsonAsync($"/api/chats/private/{bob.UserId}");
        await bobApi.PostJsonAsync($"/api/chats/{chat.Id("chatId")}/messages", new { text = "hi" });

        // An event sent before the message arrives before it on the same connection.
        await aliceGotMessage.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.DoesNotContain(bob.UserId, aliceSaw);
    }
}
