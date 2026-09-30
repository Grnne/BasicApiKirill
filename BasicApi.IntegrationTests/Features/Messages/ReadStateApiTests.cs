using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.SignalR.Client;

namespace BasicApi.IntegrationTests.Features.Messages;

/// <summary>Reading on all devices and "mark as unread" (plan 2, F2.2, D9).</summary>
public class ReadStateApiTests(PostgresFixture db) : DbTest(db)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static Dictionary<string, string?> Settings => new() { ["RateLimiting:CommandsPer10Seconds"] = "1000" };

    private static Task<HttpResponseMessage> MarkUnreadAsync(HttpClient client, Guid chatId, bool value) =>
        client.PutAsJsonAsync($"/api/chats/{chatId}/marked-unread", new { markedUnread = value });

    private static async Task ReadAsync(HttpClient client, Guid chatId, Guid messageId) =>
        (await client.PostAsJsonAsync($"/api/chats/{chatId}/read", new { lastMessageId = messageId })).EnsureSuccessStatusCode();

    [Fact]
    public async Task ReadingOnOneDevice_UpdatesTheCountersOnTheOthers()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        var bobPhone = await factory.RegisterAsync("bob");
        var bobLaptop = await factory.LoginAsync("bob");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var phoneApi = factory.CreateClient(bobPhone.Token);
        var chat = await Data.PrivateChatAsync(alice.UserId, bobPhone.UserId);
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
            ids.Add((await aliceApi.PostJsonAsync($"/api/chats/{chat}/messages", new { text = $"m{i}" })).Id());

        var states = new List<JsonElement>();
        var gotState = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var laptopHub = factory.CreateHubConnection(bobLaptop.Token);
        laptopHub.On<JsonElement>("ReadStateChanged", s =>
        {
            lock (states) states.Add(s);
            gotState.TrySetResult();
        });
        await laptopHub.StartAsync();

        await ReadAsync(phoneApi, chat, ids[1]);

        await gotState.Task.WaitAsync(Wait);
        var state = states.Single();
        Assert.Equal(chat, state.Id("chatId"));
        Assert.Equal(1, state.GetProperty("unreadCount").GetInt32());
        Assert.Equal((await phoneApi.ChatItemAsync(chat)).GetProperty("lastReadSeq").GetInt64(),
            state.GetProperty("lastReadSeq").GetInt64());
        Assert.False(state.GetProperty("markedUnread").GetBoolean());

        // A device that was offline catches up through the journal; reading an older one changes nothing.
        await ReadAsync(phoneApi, chat, ids[0]);
        Assert.Single(await phoneApi.JournalAsync("ReadStateChanged"));
        Assert.Empty(await aliceApi.JournalAsync("ReadStateChanged"));
    }

    [Fact]
    public async Task MarkUnread_IsTheUsersOwnReminder_UntilTheChatIsRead()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        var message = (await aliceApi.PostJsonAsync($"/api/chats/{chat}/messages", new { text = "hi" })).Id();
        await ReadAsync(bobApi, chat, message);

        Assert.Equal(HttpStatusCode.NoContent, (await MarkUnreadAsync(bobApi, chat, true)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await MarkUnreadAsync(bobApi, chat, true)).StatusCode);

        var item = await bobApi.ChatItemAsync(chat);
        Assert.True(item.GetProperty("markedUnread").GetBoolean());
        Assert.Equal(0, item.GetProperty("unreadCount").GetInt32());
        Assert.Contains((await bobApi.GetJsonAsync("/api/chats")).EnumerateArray(),
            c => c.Id("chatId") == chat && c.GetProperty("markedUnread").GetBoolean());
        Assert.False((await aliceApi.ChatItemAsync(chat)).GetProperty("markedUnread").GetBoolean());

        // Reading clears the mark even when there is nothing new to read.
        await ReadAsync(bobApi, chat, message);
        Assert.False((await bobApi.ChatItemAsync(chat)).GetProperty("markedUnread").GetBoolean());

        // Set once (the repeat changed nothing), cleared once; the first read moved the pointer.
        var states = await bobApi.JournalAsync("ReadStateChanged");
        Assert.Equal([false, true, false], states.Select(s => s.GetProperty("markedUnread").GetBoolean()));

        Assert.Equal(HttpStatusCode.NoContent, (await MarkUnreadAsync(bobApi, chat, true)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await MarkUnreadAsync(bobApi, chat, false)).StatusCode);
        Assert.False((await bobApi.ChatItemAsync(chat)).GetProperty("markedUnread").GetBoolean());
    }

    [Fact]
    public async Task MarkUnread_ForeignChat_Is403()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        var bob = await Data.UserAsync("bob");
        var carol = await Data.UserAsync("carol");
        using var aliceApi = factory.CreateClient(alice.Token);
        var chat = await Data.PrivateChatAsync(bob, carol);

        var response = await MarkUnreadAsync(aliceApi, chat, true);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("NOT_A_MEMBER", await response.ErrorCodeAsync());
    }
}
