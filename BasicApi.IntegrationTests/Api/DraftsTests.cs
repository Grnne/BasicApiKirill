using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.SignalR.Client;

namespace BasicApi.IntegrationTests.Api;

/// <summary>Drafts shared by the user's devices (plan 2, F2.3, D4).</summary>
public class DraftsTests(PostgresFixture db) : DbTest(db)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static Dictionary<string, string?> Settings => new() { ["RateLimiting:CommandsPer10Seconds"] = "1000" };

    private static Task<HttpResponseMessage> SaveAsync(HttpClient client, Guid chatId, object draft) =>
        client.PutAsJsonAsync($"/api/chats/{chatId}/draft", draft);

    private static async Task<JsonElement> DraftOfAsync(HttpClient client, Guid chatId) =>
        (await client.ChatItemAsync(chatId)).GetProperty("draft");

    [Fact]
    public async Task Draft_FollowsTheUserAcrossDevices_AndSendingClearsIt()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var laptop = await factory.LoginAsync("alice");
        using var phoneApi = factory.CreateClient(alice.Token);
        using var laptopApi = factory.CreateClient(laptop.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        var question = (await bobApi.PostJsonAsync($"/api/chats/{chat}/messages", new { text = "lunch?" })).Id();

        var updates = new List<JsonElement>();
        var gotTwo = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var laptopHub = factory.CreateHubConnection(laptop.Token);
        laptopHub.On<JsonElement>("DraftUpdated", d =>
        {
            lock (updates)
            {
                updates.Add(d);
                if (updates.Count == 2) gotTwo.TrySetResult();
            }
        });
        await laptopHub.StartAsync();

        // Typed on the phone: kept as typed, spaces included, with formatting and a reply.
        var saved = await SaveAsync(phoneApi, chat, new
        {
            text = "  sure, at *noon* ",
            entities = new[] { new { type = "bold", offset = 12, length = 6 } },
            replyToMessageId = question
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var draft = await saved.ReadJsonAsync();
        Assert.Equal("  sure, at *noon* ", draft.GetProperty("text").GetString());
        Assert.Equal(12, draft.GetProperty("entities")[0].GetProperty("offset").GetInt32());

        // The same again: nothing new for the other devices.
        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(phoneApi, chat, new
        {
            text = "  sure, at *noon* ",
            entities = new[] { new { type = "bold", offset = 12, length = 6 } },
            replyToMessageId = question
        })).StatusCode);

        // The laptop finds it in the chat list, and nobody else sees it.
        var onLaptop = await DraftOfAsync(laptopApi, chat);
        Assert.Equal("  sure, at *noon* ", onLaptop.GetProperty("text").GetString());
        Assert.Equal(question, onLaptop.Id("replyToMessageId"));
        Assert.Equal(JsonValueKind.Null, (await DraftOfAsync(bobApi, chat)).ValueKind);
        var state = await laptopApi.GetJsonAsync("/api/sync/state");
        Assert.Contains(state.GetProperty("chats").EnumerateArray(),
            c => c.Id("chatId") == chat && c.GetProperty("draft").ValueKind == JsonValueKind.Object);

        // Sent from the laptop: the draft is gone everywhere.
        (await laptopApi.PostJsonAsync($"/api/chats/{chat}/messages", new { text = "sure, at noon" })).GetProperty("id");
        Assert.Equal(JsonValueKind.Null, (await DraftOfAsync(phoneApi, chat)).ValueKind);

        await gotTwo.Task.WaitAsync(Wait);
        lock (updates)
        {
            Assert.Equal(JsonValueKind.Object, updates[0].GetProperty("draft").ValueKind);
            Assert.Equal(JsonValueKind.Null, updates[1].GetProperty("draft").ValueKind);
            Assert.All(updates, u => Assert.Equal(chat, u.Id("chatId")));
        }
        Assert.Equal(2, (await phoneApi.JournalAsync("DraftUpdated")).Count);
        Assert.Empty(await bobApi.JournalAsync("DraftUpdated"));
    }

    [Fact]
    public async Task EmptyDraft_RemovesIt_AndRemovingNothingIsQuiet()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        var bob = await Data.UserAsync("bob");
        using var api = factory.CreateClient(alice.Token);
        var chat = await Data.PrivateChatAsync(alice.UserId, bob);

        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(api, chat, new { text = "draft" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SaveAsync(api, chat, new { text = "   " })).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await DraftOfAsync(api, chat)).ValueKind);
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteAsync($"/api/chats/{chat}/draft")).StatusCode);

        // Only a reply, no text yet — still a draft.
        var message = await Data.MessageAsync(chat, bob, "hi", TestData.T0);
        Assert.Equal(HttpStatusCode.OK, (await SaveAsync(api, chat, new { text = "", replyToMessageId = message })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteAsync($"/api/chats/{chat}/draft")).StatusCode);

        Assert.Equal([true, false, true, false],
            (await api.JournalAsync("DraftUpdated")).Select(d => d.GetProperty("draft").ValueKind == JsonValueKind.Object));
    }

    [Fact]
    public async Task Draft_IsCheckedLikeAMessage()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        var bob = await Data.UserAsync("bob");
        var carol = await Data.UserAsync("carol");
        using var api = factory.CreateClient(alice.Token);
        var chat = await Data.PrivateChatAsync(alice.UserId, bob);
        var foreignChat = await Data.PrivateChatAsync(bob, carol);
        var foreign = await Data.MessageAsync(foreignChat, carol, "secret", TestData.T0);

        async Task Expect(HttpStatusCode status, string code, Guid chatId, object draft)
        {
            var response = await SaveAsync(api, chatId, draft);
            Assert.Equal(status, response.StatusCode);
            Assert.Equal(code, await response.ErrorCodeAsync());
        }

        await Expect(HttpStatusCode.BadRequest, "MESSAGE_TOO_LONG", chat, new { text = new string('x', 4097) });
        await Expect(HttpStatusCode.BadRequest, "INVALID_ENTITIES", chat,
            new { text = "click", entities = new[] { new { type = "link", offset = 0, length = 5, url = "javascript:alert(1)" } } });
        await Expect(HttpStatusCode.BadRequest, "INVALID_ENTITIES", chat,
            new { text = "@carol", entities = new[] { new { type = "mention", offset = 0, length = 6, userId = carol } } });
        await Expect(HttpStatusCode.BadRequest, "REPLY_TARGET_NOT_FOUND", chat, new { text = "re", replyToMessageId = foreign });
        await Expect(HttpStatusCode.Forbidden, "NOT_A_MEMBER", foreignChat, new { text = "peek" });
        Assert.Empty(await api.JournalAsync("DraftUpdated"));
    }

    [Fact]
    public async Task LeavingTheChat_TakesTheDraftAlong()
    {
        var alice = await Data.UserAsync("alice");
        var bob = await Data.UserAsync("bob");
        var chat = await Data.GroupChatAsync("team", [alice, bob]);
        await using var connection = new Npgsql.NpgsqlConnection(Db.ConnectionString);
        await connection.OpenAsync();
        await using (var insert = new Npgsql.NpgsqlCommand(
            "INSERT INTO user_drafts (user_id, chat_id, text, updated_at) VALUES (@u, @c, 'bye', now())", connection))
        {
            insert.Parameters.AddWithValue("u", alice);
            insert.Parameters.AddWithValue("c", chat);
            await insert.ExecuteNonQueryAsync();
        }

        await using (var leave = new Npgsql.NpgsqlCommand(
            "DELETE FROM chat_members WHERE chat_id = @c AND user_id = @u", connection))
        {
            leave.Parameters.AddWithValue("u", alice);
            leave.Parameters.AddWithValue("c", chat);
            await leave.ExecuteNonQueryAsync();
        }

        await using var count = new Npgsql.NpgsqlCommand("SELECT COUNT(*) FROM user_drafts", connection);
        Assert.Equal(0L, await count.ExecuteScalarAsync());
    }
}
