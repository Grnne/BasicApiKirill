using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;
using BasicApi.Models;
using Microsoft.AspNetCore.SignalR.Client;

namespace BasicApi.IntegrationTests.Platform;

/// <summary>A client that stops reading must not hold up the events of everybody else.</summary>
public class SlowClientTests(PostgresFixture db) : DbTest(db)
{
    private const char RecordSeparator = '\u001e';

    [Fact]
    public async Task ClientThatStopsReading_DoesNotDelayOthersEvents()
    {
        // Found by the review: the dispatcher sent one event at a time and waited for every
        // connection to take it. A connection whose buffer was full (a phone gone off the network,
        // or a client that pings but never reads) held up live events for all users.
        await using var factory = new ApiFactory(Db.ConnectionString,
            new Dictionary<string, string?> { ["RateLimiting:CommandsPer10Seconds"] = "1000" });
        var alice = await factory.RegisterAsync("alice");
        var mallory = await factory.RegisterAsync("mallory");
        var carol = await factory.RegisterAsync("carol");
        var dave = await factory.RegisterAsync("dave");
        var flooded = await Data.GroupChatAsync("flood", [alice.UserId, mallory.UserId]);
        var quiet = await Data.GroupChatAsync("quiet", [carol.UserId, dave.UserId]);

        await StallAsync(factory, mallory.Token, flooded);
        await using var daveHub = factory.CreateHubConnection(dave.Token);
        var daveGotIt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        daveHub.On<Guid, JsonElement>("ChatListUpdated", (chatId, _) => { if (chatId == quiet) daveGotIt.TrySetResult(); });
        await daveHub.StartAsync();

        // Well over the 64 KB a connection buffers.
        using var aliceApi = factory.CreateClient(alice.Token);
        var text = new string('x', MessageText.MaxLength);
        for (var i = 0; i < 40; i++)
            (await aliceApi.PostAsJsonAsync($"/api/chats/{flooded}/messages", new { text })).EnsureSuccessStatusCode();

        using var carolApi = factory.CreateClient(carol.Token);
        var clock = Stopwatch.StartNew();
        (await carolApi.PostAsJsonAsync($"/api/chats/{quiet}/messages", new { text = "hi" })).EnsureSuccessStatusCode();
        await daveGotIt.Task.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"Dave waited {clock.Elapsed.TotalSeconds:F1} s");
    }

    /// <summary>A long-polling client that connects, joins the chat and then never polls again.</summary>
    private static async Task StallAsync(ApiFactory factory, string token, Guid chatId)
    {
        using var api = factory.CreateClient(token);
        var negotiate = await (await api.PostAsync("/hubs/chat/negotiate?negotiateVersion=1", null)).ReadJsonAsync();
        var id = Uri.EscapeDataString(negotiate.GetProperty("connectionToken").GetString()!);

        await SendAsync(api, id, """{"protocol":"json","version":1}""");
        (await api.GetAsync($"/hubs/chat?id={id}")).EnsureSuccessStatusCode(); // the handshake answer
        await SendAsync(api, id, $$"""{"type":1,"target":"JoinChat","arguments":["{{chatId}}"]}""");
        // Long enough for the join to be done; the completion is never read.
        await Task.Delay(500);
    }

    private static async Task SendAsync(HttpClient api, string id, string message) =>
        (await api.PostAsync($"/hubs/chat?id={id}", new StringContent(message + RecordSeparator, Encoding.UTF8)))
            .EnsureSuccessStatusCode();
}
