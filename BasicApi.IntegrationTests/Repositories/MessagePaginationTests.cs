using BasicApi.IntegrationTests.Infrastructure;
using BasicApi.Storage.Repositories;

namespace BasicApi.IntegrationTests.Repositories;

public class MessagePaginationTests(PostgresFixture db) : DbTest(db)
{
    private MessageRepository Repository => new(NewSession());

    [Fact]
    public async Task Pagination_WithIdenticalCreatedAt_ReturnsEveryMessageOnce()
    {
        // Messages with identical timestamps are common with batch inserts.
        // A seq cursor must go through all of them with no gaps or repeats.
        var alice = await Data.UserAsync("alice");
        var bob = await Data.UserAsync("bob");
        var chat = await Data.PrivateChatAsync(alice, bob);

        var expected = new List<Guid>();
        for (var i = 0; i < 7; i++)
            expected.Add(await Data.MessageAsync(chat, alice, $"m{i}", TestData.T0));

        var seen = new List<Guid>();
        long? beforeSeq = null;
        var pages = 0;
        do
        {
            var page = await Repository.GetMessagesWithSenderCursorAsync(chat, beforeSeq, limit: 3);
            seen.AddRange(page.Items.Select(m => m.Id));
            beforeSeq = page.HasMore ? page.Items[^1].Seq : null;
            pages++;
        } while (beforeSeq is not null && pages < 10);

        Assert.Equal(3, pages);
        Assert.Equal(expected.Count, seen.Distinct().Count());
        Assert.Equal(Enumerable.Reverse(expected), seen); // newest to oldest — in send order
    }

    [Fact]
    public async Task Pagination_ReturnsNewestFirst_AndSkipsDeleted()
    {
        var alice = await Data.UserAsync("alice", "Alice");
        var bob = await Data.UserAsync("bob");
        var chat = await Data.PrivateChatAsync(alice, bob);

        var old = await Data.MessageAsync(chat, alice, "old", TestData.T0);
        await Data.MessageAsync(chat, bob, "deleted", TestData.T0.AddMinutes(1), isDeleted: true);
        var recent = await Data.MessageAsync(chat, bob, "recent", TestData.T0.AddMinutes(2));

        var page = await Repository.GetMessagesWithSenderCursorAsync(chat, beforeSeq: null, limit: 10);

        Assert.Equal([recent, old], page.Items.Select(m => m.Id));
        Assert.False(page.HasMore);
        Assert.Equal("Alice", page.Items[1].SenderName);
        Assert.Equal(TestData.T0, page.Items[1].CreatedAt);
    }

    [Fact]
    public async Task Pagination_DoesNotLeakMessagesFromOtherChats()
    {
        var alice = await Data.UserAsync("alice");
        var bob = await Data.UserAsync("bob");
        var carol = await Data.UserAsync("carol");
        var chat = await Data.PrivateChatAsync(alice, bob);
        var other = await Data.PrivateChatAsync(alice, carol);

        var mine = await Data.MessageAsync(chat, alice, "here", TestData.T0);
        await Data.MessageAsync(other, carol, "elsewhere", TestData.T0);

        var page = await Repository.GetMessagesWithSenderCursorAsync(chat, beforeSeq: null, limit: 10);

        Assert.Equal([mine], page.Items.Select(m => m.Id));
    }
}
