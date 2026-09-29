using BasicApi.Services;

namespace BasicApi.Tests.Services;

public class UserStatusServicePresenceTests
{
    [Fact]
    public async Task ConcurrentLastDisconnectAndNewConnect_NeverLosesTheNewConnection()
    {
        // The tab reloads: the old connection closes and the new one opens
        // at the same time. Previously the new one could end up in a set that was just being removed
        // from the dictionary — and the user "went offline" while still connected.
        var failures = new List<string>();

        for (var i = 0; i < 5_000; i++)
        {
            var service = new UserStatusService();
            var user = Guid.NewGuid();
            await service.SetUserOnlineStatusAsync(user, "old", true);

            using var start = new Barrier(2);
            var wentOfflineTask = Task.Run(() =>
            {
                start.SignalAndWait();
                return service.SetUserOnlineStatusAsync(user, "old", false);
            });
            var cameOnlineTask = Task.Run(() =>
            {
                start.SignalAndWait();
                return service.SetUserOnlineStatusAsync(user, "new", true);
            });

            var wentOffline = await wentOfflineTask;
            var cameOnline = await cameOnlineTask;
            var count = await service.GetConnectionCountAsync(user);
            var online = (await service.GetOnlineUserIdsAsync(new HashSet<Guid> { user })).Contains(user);

            // The result must be "online, one connection", and the events must be paired:
            // either "went offline" + "came online", or none.
            if (!online || count != 1 || wentOffline != cameOnline)
                failures.Add($"#{i}: online={online} count={count} wentOffline={wentOffline} cameOnline={cameOnline}");
        }

        Assert.Empty(failures);
    }

    [Fact]
    public async Task ConcurrentConnects_ReportFirstConnectionExactlyOnce()
    {
        for (var i = 0; i < 2_000; i++)
        {
            var service = new UserStatusService();
            var user = Guid.NewGuid();

            var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(n =>
                Task.Run(() => service.SetUserOnlineStatusAsync(user, $"c{n}", true))));

            Assert.Single(results, first => first);
            Assert.Equal(4, await service.GetConnectionCountAsync(user));
        }
    }
}
