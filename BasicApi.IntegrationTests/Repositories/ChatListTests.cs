using BasicApi.IntegrationTests.Infrastructure;
using BasicApi.Storage.Repositories;

namespace BasicApi.IntegrationTests.Repositories;

public class ChatListTests(PostgresFixture db) : DbTest(db)
{
    private ChatRepository Repository => new(Db.ConnectionFactory);

    [Fact]
    public async Task ChatList_ShowsCompanionAndLastMessage()
    {
        var alice = await Data.UserAsync("alice", "Alice");
        var bob = await Data.UserAsync("bob", "Bob");
        var chat = await Data.PrivateChatAsync(alice, bob);

        await Data.MessageAsync(chat, alice, "first", TestData.T0);
        var last = await Data.MessageAsync(chat, bob, "second", TestData.T0.AddMinutes(1));
        await Data.MessageAsync(chat, bob, "deleted", TestData.T0.AddMinutes(2), isDeleted: true);

        var item = Assert.Single(await Repository.GetUserChatsBatchedAsync(alice));

        Assert.Equal(chat, item.ChatId);
        Assert.Equal("private", item.Type);
        Assert.Equal(bob, item.CompanionId);
        Assert.Equal("Bob", item.CompanionName);
        Assert.Equal("bob", item.CompanionUsername);
        Assert.Equal(last, item.LastMessageId);
        Assert.Equal("second", item.LastMessageText);
        Assert.Equal("Bob", item.LastMessageSenderName);
        Assert.Equal(TestData.T0.AddMinutes(1), item.LastMessageCreatedAt);
    }

    [Fact]
    public async Task ChatList_IsOrderedByLastActivity()
    {
        var alice = await Data.UserAsync("alice");
        var bob = await Data.UserAsync("bob");
        var carol = await Data.UserAsync("carol");

        var quiet = await Data.PrivateChatAsync(alice, bob, TestData.T0);
        var busy = await Data.PrivateChatAsync(alice, carol, TestData.T0);
        var fresh = await Data.GroupChatAsync("fresh, no messages", [alice, bob], TestData.T0.AddHours(1));

        await Data.MessageAsync(quiet, bob, "long ago", TestData.T0.AddMinutes(5));
        await Data.MessageAsync(busy, carol, "just now", TestData.T0.AddHours(2));

        var list = await Repository.GetUserChatsBatchedAsync(alice);

        Assert.Equal([busy, fresh, quiet], list.Select(c => c.ChatId));
    }

    [Fact]
    public async Task ChatList_UnreadCount_CountsMessagesAfterReadPointer()
    {
        var alice = await Data.UserAsync("alice");
        var bob = await Data.UserAsync("bob");
        var chat = await Data.PrivateChatAsync(alice, bob);

        var read = await Data.MessageAsync(chat, bob, "1", TestData.T0);
        await Data.MessageAsync(chat, bob, "2", TestData.T0.AddMinutes(1));
        await Data.MessageAsync(chat, bob, "3", TestData.T0.AddMinutes(2));
        await Data.MessageAsync(chat, bob, "deleted", TestData.T0.AddMinutes(3), isDeleted: true);

        Assert.Equal(3, Assert.Single(await Repository.GetUserChatsBatchedAsync(alice)).UnreadCount);

        await Data.MarkReadAsync(chat, alice, read);

        Assert.Equal(2, Assert.Single(await Repository.GetUserChatsBatchedAsync(alice)).UnreadCount);
    }

    [Fact]
    public async Task ChatList_ContainsOnlyChatsOfTheUser()
    {
        var alice = await Data.UserAsync("alice");
        var bob = await Data.UserAsync("bob");
        var carol = await Data.UserAsync("carol");
        var mine = await Data.PrivateChatAsync(alice, bob);
        await Data.PrivateChatAsync(bob, carol);

        var list = await Repository.GetUserChatsBatchedAsync(alice);

        Assert.Equal([mine], list.Select(c => c.ChatId));
        Assert.Null(await Repository.GetChatListItemAsync(mine, carol));
    }
}
