using BasicApi.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.SignalR.Client;

namespace BasicApi.IntegrationTests.Api;

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
}
