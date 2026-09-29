using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;
using BasicApi.Services.Events;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Interfaces;
using Dapper;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BasicApi.IntegrationTests.Api;

/// <summary>Outbox: the event is saved together with the change and is not lost (plan 1, 2.6).</summary>
public class OutboxTests(PostgresFixture db) : DbTest(db)
{
    private static readonly Dictionary<string, string?> NoDispatcher = new() { ["Outbox:DispatcherEnabled"] = "false" };

    private async Task<long> CountAsync(string sql, object? param = null)
    {
        await using var connection = new NpgsqlConnection(Db.ConnectionString);
        return await connection.ExecuteScalarAsync<long>(sql, param);
    }

    [Fact]
    public async Task CrashBetweenSavingAndSending_DoesNotLoseTheEvent()
    {
        Guid chat, messageId;
        string bobToken;

        // The process saved the message and "crashed" before it could dispatch the event.
        await using (var crashed = new ApiFactory(Db.ConnectionString, NoDispatcher))
        {
            var alice = await crashed.RegisterAsync("alice");
            var bob = await crashed.RegisterAsync("bob");
            bobToken = bob.Token;
            chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);

            using var client = crashed.CreateClient(alice.Token);
            var response = await client.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text = "survives" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            messageId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        }

        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM outbox WHERE processed_at IS NULL"));

        // After a restart the event goes out on the very first dispatch.
        await using var restarted = new ApiFactory(Db.ConnectionString, NoDispatcher);
        var created = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listUpdated = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var bobHub = restarted.CreateHubConnection(bobToken);
        bobHub.On<JsonElement>("MessageCreated", m => created.TrySetResult(m));
        bobHub.On<Guid, JsonElement>("ChatListUpdated", (c, _) => listUpdated.TrySetResult(c));
        await bobHub.StartAsync();
        await bobHub.InvokeAsync("JoinChat", chat);

        var result = await restarted.Services.GetRequiredService<OutboxDispatcher>().DispatchPendingAsync();

        Assert.Equal(new DispatchResult(1, Failed: false), result);
        var delivered = await created.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(messageId, delivered.GetProperty("id").GetGuid());
        Assert.Equal("survives", delivered.GetProperty("text").GetString());
        Assert.Equal(chat, await listUpdated.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM outbox WHERE processed_at IS NULL"));

        // A repeated dispatch sends nothing.
        Assert.Equal(new DispatchResult(0, Failed: false),
            await restarted.Services.GetRequiredService<OutboxDispatcher>().DispatchPendingAsync());
    }

    [Fact]
    public async Task MessageAndItsEvent_AreSavedTogether_OrNotAtAll()
    {
        // Writing the event is broken - the message must not be left without an event.
        await using var factory = new ApiFactory(Db.ConnectionString,
            services: s => s.AddScoped<IOutboxRepository, FailingOutbox>());
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        using var client = factory.CreateClient(alice.Token);

        var response = await client.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text = "all or nothing" });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM messages"));
        Assert.Equal(0, await CountAsync("SELECT last_seq FROM chats WHERE id = @chat", new { chat }));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM user_updates"));
    }

    [Fact]
    public async Task NewMessage_IsJournaledForEveryMember()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        using var client = factory.CreateClient(alice.Token);

        for (var i = 0; i < 3; i++)
            (await client.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text = $"m{i}" })).EnsureSuccessStatusCode();

        await using var connection = new NpgsqlConnection(Db.ConnectionString);
        foreach (var user in new[] { alice.UserId, bob.UserId })
        {
            var journal = (await connection.QueryAsync<(long Pts, string Type, string Text)>(@"
                SELECT pts, type, payload->>'text' FROM user_updates WHERE user_id = @user ORDER BY pts",
                new { user })).ToList();
            Assert.Equal([(1L, "MessageCreated", "m0"), (2L, "MessageCreated", "m1"), (3L, "MessageCreated", "m2")], journal);
        }
    }

    private sealed class FailingOutbox : IOutboxRepository
    {
        public Task EnqueueAsync(string type, string payloadJson, CancellationToken ct = default) =>
            throw new InvalidOperationException("outbox is broken");

        public Task<IReadOnlyList<OutboxRow>> LockPendingAsync(int limit, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OutboxRow>>([]);

        public Task MarkProcessedAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default) => Task.CompletedTask;

        public Task<int> MarkFailedAsync(long id, int giveUpAfter, CancellationToken ct = default) => Task.FromResult(0);

        public Task<int> DeleteProcessedOlderThanAsync(DateTime olderThan, int batchSize, CancellationToken ct = default) =>
            Task.FromResult(0);
    }
}
