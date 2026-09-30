using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;
using Dapper;
using Microsoft.AspNetCore.SignalR.Client;
using Npgsql;

namespace BasicApi.IntegrationTests.Api;

/// <summary>Creating a group and its system messages (plan 2, F3.1).</summary>
public class GroupCreationTests(PostgresFixture db) : DbTest(db)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static Dictionary<string, string?> Settings(int maxMembers = 500) => new()
    {
        ["RateLimiting:CommandsPer10Seconds"] = "1000",
        ["Groups:MaxMembers"] = maxMembers.ToString()
    };

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, string? title, params Guid[] memberIds) =>
        client.PostAsJsonAsync("/api/chats/groups", new { title, memberIds });

    [Fact]
    public async Task Group_IsCreated_WithTheCreatorAsOwner_AndEveryMemberHearsOfIt()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings());
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carol = await factory.RegisterAsync("carol");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);

        var created = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var bobHub = factory.CreateHubConnection(bob.Token);
        bobHub.On<JsonElement>("ChatCreated", c => created.TrySetResult(c));
        await bobHub.StartAsync();

        // Duplicates and the creator in the list are fine.
        var response = await CreateAsync(aliceApi, "  Launch team ", bob.UserId, carol.UserId, bob.UserId, alice.UserId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var group = await response.ReadJsonAsync();
        var chatId = group.Id("chatId");
        Assert.Equal("group", group.GetProperty("type").GetString());
        Assert.Equal("Launch team", group.GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, group.GetProperty("companionId").ValueKind);
        Assert.Equal(0, group.GetProperty("unreadCount").GetInt32());
        var opening = group.GetProperty("lastMessage");
        Assert.Equal("Создана группа «Launch team»", opening.GetProperty("text").GetString());

        // Bob got his card live: the opening message is his one unread.
        var bobsCard = await created.Task.WaitAsync(Wait);
        Assert.Equal(chatId, bobsCard.Id("chatId"));
        Assert.Equal(1, bobsCard.GetProperty("unreadCount").GetInt32());

        var details = await bobApi.GetJsonAsync($"/api/chats/{chatId}");
        Assert.Equal(
            [(alice.UserId, "owner"), (bob.UserId, "member"), (carol.UserId, "member")],
            details.GetProperty("participants").EnumerateArray()
                .Select(p => (p.Id("userId"), p.GetProperty("role").GetString()))
                .OrderBy(p => p.Item2 == "owner" ? 0 : 1).ThenBy(p => p.Item1 == bob.UserId ? 0 : 1));

        // The history starts with the system message; everyone can write.
        var history = await bobApi.HistoryAsync(chatId);
        var system = Assert.Single(history);
        Assert.Equal("system", system.GetProperty("type").GetString());
        Assert.Equal(alice.UserId, system.Id("senderId"));
        Assert.Equal("group_created", system.GetProperty("action").GetProperty("type").GetString());
        Assert.Equal("Launch team", system.GetProperty("action").GetProperty("title").GetString());
        (await bobApi.PostJsonAsync($"/api/chats/{chatId}/messages", new { text = "hi all" })).GetProperty("id");

        // The creator's other devices learn of it through sync; Carol, offline, too.
        using var carolApi = factory.CreateClient(carol.Token);
        Assert.Single(await aliceApi.JournalAsync("ChatCreated"), c => c.Id("chatId") == chatId);
        Assert.Single(await carolApi.JournalAsync("ChatCreated"), c => c.Id("chatId") == chatId);

        // After the card, the opening message comes whole: the card has only its preview (plan 2, F8).
        var carolsUpdates = (await carolApi.GetJsonAsync("/api/sync?since=0")).GetProperty("updates").EnumerateArray().ToList();
        Assert.Equal(["ChatCreated", "MessageCreated", "MessageCreated"], carolsUpdates.Select(u => u.GetProperty("type").GetString()));
        var journaled = carolsUpdates[1].GetProperty("payload");
        Assert.Equal(system.Id(), journaled.Id());
        Assert.Equal("group_created", journaled.GetProperty("action").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Group_OfOne_IsFine()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings());
        var alice = await factory.RegisterAsync("alice");
        using var api = factory.CreateClient(alice.Token);

        var group = await (await CreateAsync(api, "Notes")).ReadJsonAsync();

        (await api.PostJsonAsync($"/api/chats/{group.Id("chatId")}/messages", new { text = "first" })).GetProperty("id");
        Assert.Contains((await api.GetJsonAsync("/api/chats")).EnumerateArray(), c => c.Id("chatId") == group.Id("chatId"));
    }

    [Fact]
    public async Task Group_Creation_IsChecked()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings(maxMembers: 3));
        var alice = await factory.RegisterAsync("alice");
        var bob = await Data.UserAsync("bob");
        var carol = await Data.UserAsync("carol");
        var dave = await Data.UserAsync("dave");
        var gone = await Data.UserAsync("gone", isActive: false);
        using var api = factory.CreateClient(alice.Token);

        async Task Expect(HttpStatusCode status, string code, string? title, params Guid[] members)
        {
            var response = await CreateAsync(api, title, members);
            Assert.Equal(status, response.StatusCode);
            Assert.Equal(code, await response.ErrorCodeAsync());
        }

        await Expect(HttpStatusCode.BadRequest, "INVALID_TITLE", "   ", bob);
        await Expect(HttpStatusCode.BadRequest, "INVALID_TITLE", null, bob);
        await Expect(HttpStatusCode.BadRequest, "INVALID_TITLE", new string('x', 129), bob);
        await Expect(HttpStatusCode.BadRequest, "TOO_MANY_MEMBERS", "team", bob, carol, dave);
        await Expect(HttpStatusCode.NotFound, "USER_NOT_FOUND", "team", bob, Guid.NewGuid());
        await Expect(HttpStatusCode.NotFound, "USER_NOT_FOUND", "team", bob, gone);

        Assert.Equal(HttpStatusCode.Created, (await CreateAsync(api, new string('x', 128), bob, carol)).StatusCode);
        await using var connection = new NpgsqlConnection(Db.ConnectionString);
        Assert.Equal(1, await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM chats"));
    }

    [Fact]
    public async Task SystemMessages_CannotBeEditedAnsweredForwardedOrFound()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings());
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var api = factory.CreateClient(alice.Token);
        var chatId = (await (await CreateAsync(api, "Launch", bob.UserId)).ReadJsonAsync()).Id("chatId");
        var system = (await api.HistoryAsync(chatId)).Single().Id();
        var saved = (await api.PostJsonAsync("/api/chats/saved")).Id("chatId");

        var edit = await api.PatchAsJsonAsync($"/api/chats/{chatId}/messages/{system}", new { text = "hacked" });
        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
        Assert.Equal("MESSAGE_NOT_EDITABLE", await edit.ErrorCodeAsync());

        var reply = await api.PostAsJsonAsync($"/api/chats/{chatId}/messages", new { text = "re", replyToMessageId = system });
        Assert.Equal("REPLY_TARGET_NOT_FOUND", await reply.ErrorCodeAsync());
        var draft = await api.PutAsJsonAsync($"/api/chats/{chatId}/draft", new { text = "re", replyToMessageId = system });
        Assert.Equal("REPLY_TARGET_NOT_FOUND", await draft.ErrorCodeAsync());

        var forward = await api.PostAsJsonAsync($"/api/chats/{saved}/messages/forward",
            new { fromChatId = chatId, messageIds = new[] { system } });
        Assert.Equal(HttpStatusCode.NotFound, forward.StatusCode);

        var search = await api.GetJsonAsync($"/api/chats/{chatId}/messages/search?q=Launch");
        Assert.Equal(0, search.GetProperty("totalCount").GetInt32());
    }
}
