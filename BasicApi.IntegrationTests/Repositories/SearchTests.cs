using BasicApi.IntegrationTests.Infrastructure;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Repositories;

namespace BasicApi.IntegrationTests.Repositories;

public class SearchTests(PostgresFixture db) : DbTest(db)
{
    [Fact]
    public async Task SearchChats_FindsPrivateByCompanionAndGroupByTitle_CaseInsensitive()
    {
        var repository = new ChatRepository(Db.ConnectionFactory);
        var alice = await Data.UserAsync("alice");
        var bob = await Data.UserAsync("bob_builder", "Bob Builder");
        var carol = await Data.UserAsync("carol", "Carol");

        var withBob = await Data.PrivateChatAsync(alice, bob);
        await Data.PrivateChatAsync(alice, carol);
        var group = await Data.GroupChatAsync("Builders club", [alice, carol]);

        var all = await repository.SearchChatsBatchedAsync(alice, "BUILD", typeFilter: null, limit: 10);
        Assert.Equal(new[] { withBob, group }.Order(), all.Select(c => c.ChatId).Order());
        Assert.Equal(2, await repository.CountChatsByQueryAsync(alice, "BUILD", typeFilter: null));

        var onlyPrivate = await repository.SearchChatsBatchedAsync(alice, "build", "private", limit: 10);
        Assert.Equal([withBob], onlyPrivate.Select(c => c.ChatId));

        var onlyGroups = await repository.SearchChatsBatchedAsync(alice, "build", "group", limit: 10);
        Assert.Equal([group], onlyGroups.Select(c => c.ChatId));
        Assert.Equal(1, await repository.CountChatsByQueryAsync(alice, "build", "group"));
    }

    [Fact]
    public async Task SearchChats_DoesNotSeeChatsOfOtherUsers()
    {
        var repository = new ChatRepository(Db.ConnectionFactory);
        var alice = await Data.UserAsync("alice");
        var bob = await Data.UserAsync("bob");
        await Data.GroupChatAsync("Secret club", [bob]);

        Assert.Empty(await repository.SearchChatsBatchedAsync(alice, "secret", typeFilter: null, limit: 10));
        Assert.Equal(0, await repository.CountChatsByQueryAsync(alice, "secret", typeFilter: null));
    }

    [Fact]
    public async Task SearchMessages_MatchesWordForms_AndPaginates()
    {
        var repository = new MessageRepository(Db.ConnectionFactory);
        var alice = await Data.UserAsync("alice");
        var bob = await Data.UserAsync("bob");
        var chat = await Data.PrivateChatAsync(alice, bob);
        var other = await Data.PrivateChatAsync(bob, await Data.UserAsync("carol"));

        var first = await Data.MessageAsync(chat, alice, "I was running late", TestData.T0);
        await Data.MessageAsync(chat, bob, "unrelated text", TestData.T0.AddMinutes(1));
        var second = await Data.MessageAsync(chat, bob, "runs every morning", TestData.T0.AddMinutes(2));
        var third = await Data.MessageAsync(chat, alice, "let's run", TestData.T0.AddMinutes(3));
        await Data.MessageAsync(chat, alice, "run (deleted)", TestData.T0.AddMinutes(4), isDeleted: true);
        await Data.MessageAsync(other, bob, "run in another chat", TestData.T0.AddMinutes(5));

        var (page1, total) = await repository.SearchMessagesCursorAsync(chat, "run", cursor: null, limit: 2);

        Assert.Equal(3, total);
        Assert.Equal([third, second], page1.Items.Select(m => m.Id));
        Assert.True(page1.HasMore);

        var cursor = new CursorDto(page1.Items[^1].CreatedAt, page1.Items[^1].Id).Encode();
        var (page2, _) = await repository.SearchMessagesCursorAsync(chat, "run", cursor, limit: 2);

        Assert.Equal([first], page2.Items.Select(m => m.Id));
        Assert.False(page2.HasMore);
    }
}
