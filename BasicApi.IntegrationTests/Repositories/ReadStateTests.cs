using System.Net;
using System.Net.Http.Json;
using BasicApi.IntegrationTests.Infrastructure;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Repositories;
using Dapper;
using Npgsql;

namespace BasicApi.IntegrationTests.Repositories;

public class ReadStateTests(PostgresFixture db) : DbTest(db)
{
    private ChatRepository Chats => new(NewSession());
    private MessageRepository Messages => new(NewSession());

    private async Task<int> UnreadAsync(Guid chatId, Guid userId) =>
        (await Chats.GetChatListItemAsync(chatId, userId))!.UnreadCount;

    private async Task<Guid?> PointerAsync(Guid chatId, Guid userId)
    {
        await using var connection = new NpgsqlConnection(Db.ConnectionString);
        return await connection.ExecuteScalarAsync<Guid?>(
            "SELECT last_read_message_id FROM chat_members WHERE chat_id = @chatId AND user_id = @userId",
            new { chatId, userId });
    }

    [Fact]
    public async Task OwnMessages_AreNotCountedAsUnread()
    {
        // Раньше отправитель видел у себя «непрочитанные» — свои же сообщения.
        var alice = await Data.UserAsync("alice");
        var bob = await Data.UserAsync("bob");
        var chat = await Data.PrivateChatAsync(alice, bob);
        await Data.MessageAsync(chat, alice, "a1", TestData.T0);
        await Data.MessageAsync(chat, alice, "a2", TestData.T0.AddMinutes(1));
        await Data.MessageAsync(chat, bob, "b1", TestData.T0.AddMinutes(2));

        Assert.Equal(1, await UnreadAsync(chat, alice));
        Assert.Equal(2, await UnreadAsync(chat, bob));
        Assert.Equal(1, Assert.Single(await Chats.GetUserChatsBatchedAsync(alice)).UnreadCount);
    }

    [Fact]
    public async Task Unread_WithIdenticalTimestamps_FollowsMessageOrder()
    {
        var alice = await Data.UserAsync("alice");
        var bob = await Data.UserAsync("bob");
        var chat = await Data.PrivateChatAsync(alice, bob);
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
            ids.Add(await Data.MessageAsync(chat, bob, $"m{i}", TestData.T0));
        ids.Sort(); // порядок сообщений с равным временем — по id

        Assert.Equal(ReadPointerUpdate.Moved, await Messages.MarkReadAsync(chat, alice, ids[1]));

        Assert.Equal(1, await UnreadAsync(chat, alice));
    }

    [Fact]
    public async Task MarkRead_MessageFromAnotherChat_IsNotFound_AndPointerUnchanged()
    {
        var alice = await Data.UserAsync("alice");
        var bob = await Data.UserAsync("bob");
        var carol = await Data.UserAsync("carol");
        var chat = await Data.PrivateChatAsync(alice, bob);
        var other = await Data.PrivateChatAsync(bob, carol);
        var mine = await Data.MessageAsync(chat, bob, "hi", TestData.T0);
        var foreign = await Data.MessageAsync(other, carol, "secret", TestData.T0.AddMinutes(1));
        await Messages.MarkReadAsync(chat, alice, mine);

        Assert.Equal(ReadPointerUpdate.MessageNotFound, await Messages.MarkReadAsync(chat, alice, foreign));
        Assert.Equal(ReadPointerUpdate.MessageNotFound, await Messages.MarkReadAsync(chat, alice, Guid.NewGuid()));

        Assert.Equal(mine, await PointerAsync(chat, alice));
    }

    [Fact]
    public async Task MarkRead_OlderMessage_DoesNotMovePointerBack()
    {
        // Два устройства: старое отчиталось позже нового — прочитанное не должно «откатиться».
        var alice = await Data.UserAsync("alice");
        var bob = await Data.UserAsync("bob");
        var chat = await Data.PrivateChatAsync(alice, bob);
        var older = await Data.MessageAsync(chat, bob, "1", TestData.T0);
        var newer = await Data.MessageAsync(chat, bob, "2", TestData.T0.AddMinutes(1));

        Assert.Equal(ReadPointerUpdate.Moved, await Messages.MarkReadAsync(chat, alice, newer));
        Assert.Equal(ReadPointerUpdate.NotMoved, await Messages.MarkReadAsync(chat, alice, older));

        Assert.Equal(newer, await PointerAsync(chat, alice));
        Assert.Equal(0, await UnreadAsync(chat, alice));
    }

    [Fact]
    public async Task Api_MarkRead_WithMessageOfAnotherChat_Returns404()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var carol = await Data.UserAsync("carol");
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        var other = await Data.PrivateChatAsync(bob.UserId, carol);
        var foreign = await Data.MessageAsync(other, carol, "secret", TestData.T0);
        using var client = factory.CreateClient(alice.Token);

        var response = await client.PostAsJsonAsync($"/api/chats/{chat}/read", new { lastMessageId = foreign });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("MESSAGE_NOT_FOUND", await response.Content.ReadAsStringAsync());
    }
}
