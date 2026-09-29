using BasicApi.IntegrationTests.Infrastructure;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Repositories;

namespace BasicApi.IntegrationTests.Repositories;

public class SearchTests(PostgresFixture db) : DbTest(db)
{
    [Fact]
    public async Task SearchChats_FindsPrivateByCompanionAndGroupByTitle_CaseInsensitive()
    {
        var repository = new ChatRepository(NewSession());
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
        var repository = new ChatRepository(NewSession());
        var alice = await Data.UserAsync("alice");
        var bob = await Data.UserAsync("bob");
        await Data.GroupChatAsync("Secret club", [bob]);

        Assert.Empty(await repository.SearchChatsBatchedAsync(alice, "secret", typeFilter: null, limit: 10));
        Assert.Equal(0, await repository.CountChatsByQueryAsync(alice, "secret", typeFilter: null));
    }

    [Fact]
    public async Task SearchMessages_MatchesWordForms_AndPaginates()
    {
        var repository = new MessageRepository(NewSession());
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

        var (page1, total) = await repository.SearchMessagesCursorAsync(chat, "run", beforeSeq: null, limit: 2);

        Assert.Equal(3, total);
        Assert.Equal([third, second], page1.Items.Select(m => m.Id));
        Assert.True(page1.HasMore);

        var (page2, _) = await repository.SearchMessagesCursorAsync(chat, "run", page1.Items[^1].Seq, limit: 2);

        Assert.Equal([first], page2.Items.Select(m => m.Id));
        Assert.False(page2.HasMore);
    }

    [Theory]
    [InlineData("запуск")]
    [InlineData("Запуск")]
    [InlineData("запу")] // as you type
    public async Task SearchMessages_MatchesRussianWordForms(string query)
    {
        // With the English dictionary, Russian words matched only exactly:
        // "запуск" did not find "запускаем" or "до запуска".
        var repository = new MessageRepository(NewSession());
        var alice = await Data.UserAsync("alice");
        var bob = await Data.UserAsync("bob");
        var chat = await Data.PrivateChatAsync(alice, bob);

        var launch = await Data.MessageAsync(chat, alice, "Завтра запускаем релиз", TestData.T0);
        var before = await Data.MessageAsync(chat, bob, "Проверю всё до запуска", TestData.T0.AddMinutes(1));
        await Data.MessageAsync(chat, bob, "Обед в час", TestData.T0.AddMinutes(2));

        var (page, total) = await repository.SearchMessagesCursorAsync(chat, query, beforeSeq: null, limit: 10);

        Assert.Equal(2, total);
        Assert.Equal([before, launch], page.Items.Select(m => m.Id));
    }

    [Fact]
    public async Task SearchMessages_QueryOperatorsAreNotInterpreted()
    {
        // User input is words only: tsquery symbols do not break the query or cause a 500.
        var repository = new MessageRepository(NewSession());
        var alice = await Data.UserAsync("alice");
        var bob = await Data.UserAsync("bob");
        var chat = await Data.PrivateChatAsync(alice, bob);
        var hit = await Data.MessageAsync(chat, alice, "release notes", TestData.T0);

        foreach (var query in new[] { "release & | ! notes", "release:*", "'release'", "(notes", "!!" })
        {
            var (page, _) = await repository.SearchMessagesCursorAsync(chat, query, null, 10);
            if (query != "!!")
                Assert.Equal([hit], page.Items.Select(m => m.Id));
            else
                Assert.Empty(page.Items);
        }
    }

    [Fact]
    public async Task SearchMessages_MixedLanguages_AndStopWordsOnly()
    {
        var repository = new MessageRepository(NewSession());
        var alice = await Data.UserAsync("alice");
        var bob = await Data.UserAsync("bob");
        var chat = await Data.PrivateChatAsync(alice, bob);
        var mixed = await Data.MessageAsync(chat, alice, "Деплоим новый build сегодня", TestData.T0);

        Assert.Equal([mixed], (await repository.SearchMessagesCursorAsync(chat, "builds", null, 10)).Result.Items.Select(m => m.Id));
        Assert.Equal([mixed], (await repository.SearchMessagesCursorAsync(chat, "новые", null, 10)).Result.Items.Select(m => m.Id));

        // Only stop words — an empty result, not an error.
        var (none, total) = await repository.SearchMessagesCursorAsync(chat, "и в на", null, 10);
        Assert.Empty(none.Items);
        Assert.Equal(0, total);
    }
}
