using System.Net;
using System.Net.Http.Json;
using BasicApi.IntegrationTests.Infrastructure;

namespace BasicApi.IntegrationTests.Features.Messages;

/// <summary>A send and a removal from the group at the same moment.</summary>
public class SendRaceTests(PostgresFixture db) : DbTest(db)
{
    [Fact]
    public async Task Send_RacingTheSendersRemoval_IsRefused()
    {
        // Found by the review: the right to post and the members were read before the chat was
        // locked, so a send that waited for a removal landed after it — from someone no longer in
        // the group, and into their own journal.
        await using var factory = new ApiFactory(Db.ConnectionString);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var chatId = await Data.GroupChatAsync("team", [alice.UserId, bob.UserId]);
        using var bobApi = factory.CreateClient(bob.Token);

        // A removal in progress: the chat locked as GroupService locks it, Bob already out, not committed yet.
        var removing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var remover = NewSession();
        var removal = remover.InTransactionAsync(async ct =>
        {
            await remover.ExecuteAsync("SELECT 1 FROM chats WHERE id = @chatId FOR UPDATE", new { chatId }, ct);
            await remover.ExecuteAsync("DELETE FROM chat_members WHERE chat_id = @chatId AND user_id = @userId",
                new { chatId, userId = bob.UserId }, ct);
            removing.SetResult();
            await commit.Task;
            return 0;
        });
        await removing.Task;

        var send = bobApi.PostAsJsonAsync($"/api/chats/{chatId}/messages", new { text = "still here?" });
        await WaitForLockWaitAsync();
        commit.SetResult();
        await removal;
        var response = await send;

        Assert.False(response.IsSuccessStatusCode, $"{(int)response.StatusCode}");
        Assert.Equal(0, await NewSession().ExecuteScalarAsync<int>(
            "SELECT count(*) FROM messages WHERE chat_id = @chatId AND sender_id = @userId", new { chatId, userId = bob.UserId }));
    }

    private async Task WaitForLockWaitAsync()
    {
        var observer = NewSession();
        for (var i = 0; i < 100; i++)
        {
            if (await observer.ExecuteScalarAsync<long>(
                    "SELECT count(*) FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND datname = current_database()") > 0)
                return;
            await Task.Delay(50);
        }
        Assert.Fail("The send never waited for the chat.");
    }
}
