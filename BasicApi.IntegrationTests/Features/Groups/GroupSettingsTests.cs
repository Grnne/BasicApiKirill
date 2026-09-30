using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;
using Dapper;
using Microsoft.AspNetCore.SignalR.Client;
using Npgsql;

namespace BasicApi.IntegrationTests.Features.Groups;

/// <summary>Renaming a group, its default permissions and deleting it (plan 2, F3.4).</summary>
public class GroupSettingsTests(PostgresFixture db) : DbTest(db)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static Dictionary<string, string?> Settings => new() { ["RateLimiting:CommandsPer10Seconds"] = "1000" };

    private static Task<HttpResponseMessage> UpdateAsync(HttpClient api, Guid chatId, object body) =>
        api.PatchAsJsonAsync($"/api/chats/{chatId}", body);

    private static async Task<string?> CodeAsync(Task<HttpResponseMessage> call) => await (await call).ErrorCodeAsync();

    [Fact]
    public async Task Renaming_NeedsChangeInfo_AndEveryoneSeesIt()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        var chatId = (await aliceApi.PostJsonAsync("/api/chats/groups", new { title = "team", memberIds = new[] { bob.UserId } })).Id("chatId");

        var updated = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var bobHub = factory.CreateHubConnection(bob.Token);
        bobHub.On<JsonElement>("ChatUpdated", u => updated.TrySetResult(u));
        await bobHub.StartAsync();

        Assert.Equal("PERMISSION_DENIED", await CodeAsync(UpdateAsync(bobApi, chatId, new { title = "bob's" })));
        Assert.Equal("INVALID_TITLE", await CodeAsync(UpdateAsync(aliceApi, chatId, new { title = " " })));

        var response = await UpdateAsync(aliceApi, chatId, new { title = " Launch " });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Launch", (await response.ReadJsonAsync()).GetProperty("title").GetString());

        var heard = await updated.Task.WaitAsync(Wait);
        Assert.Equal("Launch", heard.GetProperty("title").GetString());
        var item = await bobApi.ChatItemAsync(chatId);
        Assert.Equal("Launch", item.GetProperty("title").GetString());
        Assert.Equal("Название группы изменено на «Launch»", item.GetProperty("lastMessage").GetProperty("text").GetString());

        // The same title again: nothing happens.
        (await UpdateAsync(aliceApi, chatId, new { title = "Launch" })).EnsureSuccessStatusCode();
        Assert.Single(await bobApi.JournalAsync("ChatUpdated"));
    }

    [Fact]
    public async Task ReadOnlyGroup_MembersListen_AdminsAndExceptionsSpeak()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carol = await factory.RegisterAsync("carol");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        using var carolApi = factory.CreateClient(carol.Token);
        var chatId = (await aliceApi.PostJsonAsync("/api/chats/groups",
            new { title = "news", memberIds = new[] { bob.UserId, carol.UserId } })).Id("chatId");

        Assert.Equal("PERMISSION_DENIED", await CodeAsync(UpdateAsync(bobApi, chatId, new { memberPermissions = new { sendMessages = false } })));
        Assert.Equal("INVALID_PERMISSIONS", await CodeAsync(UpdateAsync(aliceApi, chatId, new { memberPermissions = new { deleteMessages = true } })));

        var result = await (await UpdateAsync(aliceApi, chatId, new { memberPermissions = new { sendMessages = false } })).ReadJsonAsync();
        Assert.False(result.GetProperty("memberPermissions").GetProperty("sendMessages").GetBoolean());
        Assert.True(result.GetProperty("memberPermissions").GetProperty("addMembers").GetBoolean());

        // Changing another default keeps this one.
        (await UpdateAsync(aliceApi, chatId, new { memberPermissions = new { addMembers = false } })).EnsureSuccessStatusCode();
        var details = await bobApi.GetJsonAsync($"/api/chats/{chatId}");
        Assert.False(details.GetProperty("memberPermissions").GetProperty("sendMessages").GetBoolean());
        Assert.False(details.GetProperty("memberPermissions").GetProperty("addMembers").GetBoolean());

        Assert.Equal("PERMISSION_DENIED", await CodeAsync(bobApi.PostAsJsonAsync($"/api/chats/{chatId}/messages", new { text = "hi" })));
        (await aliceApi.PutAsJsonAsync($"/api/chats/{chatId}/members/{carol.UserId}/permissions", new { sendMessages = true }))
            .EnsureSuccessStatusCode();
        (await carolApi.PostJsonAsync($"/api/chats/{chatId}/messages", new { text = "guest post" })).GetProperty("id");
        (await aliceApi.PostJsonAsync($"/api/chats/{chatId}/messages", new { text = "announcement" })).GetProperty("id");
        Assert.Equal(2, (await bobApi.JournalAsync("ChatUpdated")).Count);
    }

    [Fact]
    public async Task Deleting_IsForTheOwner_AndTakesTheGroupAwayFromEveryone()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var aliceApi = factory.CreateClient(alice.Token);
        using var bobApi = factory.CreateClient(bob.Token);
        var chatId = (await aliceApi.PostJsonAsync("/api/chats/groups", new { title = "team", memberIds = new[] { bob.UserId } })).Id("chatId");
        (await aliceApi.PutAsJsonAsync($"/api/chats/{chatId}/members/{bob.UserId}/role", new { role = "admin" })).EnsureSuccessStatusCode();
        (await bobApi.PostJsonAsync($"/api/chats/{chatId}/messages", new { text = "hello" })).GetProperty("id");

        var deleted = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var bobHub = factory.CreateHubConnection(bob.Token);
        bobHub.On<JsonElement>("ChatDeleted", d => deleted.TrySetResult(d));
        await bobHub.StartAsync();
        await bobHub.InvokeAsync("JoinChat", chatId);

        Assert.Equal("PERMISSION_DENIED", await CodeAsync(bobApi.DeleteAsync($"/api/chats/{chatId}")));
        Assert.Equal(HttpStatusCode.NoContent, (await aliceApi.DeleteAsync($"/api/chats/{chatId}")).StatusCode);

        Assert.Equal(chatId, (await deleted.Task.WaitAsync(Wait)).Id("chatId"));
        Assert.Equal(HttpStatusCode.NotFound, (await bobApi.GetAsync($"/api/chats/{chatId}")).StatusCode);
        Assert.DoesNotContain((await bobApi.GetJsonAsync("/api/chats")).EnumerateArray(), c => c.Id("chatId") == chatId);
        Assert.Single(await aliceApi.JournalAsync("ChatDeleted"));

        await using var connection = new NpgsqlConnection(Db.ConnectionString);
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM messages WHERE chat_id = @chatId", new { chatId }));
    }
}
