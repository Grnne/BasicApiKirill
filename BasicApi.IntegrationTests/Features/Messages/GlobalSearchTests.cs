using System.Net;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;
using Dapper;
using Npgsql;

namespace BasicApi.IntegrationTests.Features.Messages;

/// <summary>Search across all the user's chats (plan 2, F6.3, D10).</summary>
public class GlobalSearchTests(PostgresFixture db) : DbTest(db)
{
    private static Dictionary<string, string?> Settings => new() { ["RateLimiting:CommandsPer10Seconds"] = "1000" };

    private static async Task<JsonElement> SearchAsync(HttpClient api, string query) =>
        await api.GetJsonAsync($"/api/search/messages?{query}");

    private static List<Guid> Ids(JsonElement result) =>
        [.. result.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("message").Id())];

    [Fact]
    public async Task FindsInEveryChatOfTheUser_NewestFirst_WithTheChat()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        using var api = factory.CreateClient(alice.Token);
        var bob = await Data.UserAsync("bob", "Bob");
        var carl = await Data.UserAsync("carl");
        var withBob = await Data.PrivateChatAsync(alice.UserId, bob);
        var group = await Data.GroupChatAsync("Команда", [carl, alice.UserId]);
        var foreign = await Data.PrivateChatAsync(bob, carl);

        var first = await Data.MessageAsync(withBob, bob, "Запуск проекта в понедельник", TestData.T0.AddMinutes(1));
        var second = await Data.MessageAsync(group, carl, "готовим запуск", TestData.T0.AddMinutes(2));
        await Data.MessageAsync(foreign, bob, "запуск без Алисы", TestData.T0.AddMinutes(3));
        var hidden = await Data.MessageAsync(withBob, bob, "запуск скрытый", TestData.T0.AddMinutes(4));
        await Data.MessageAsync(group, carl, "запуск удалённый", TestData.T0.AddMinutes(5), isDeleted: true);
        await Data.MessageAsync(withBob, alice.UserId, "что-то другое", TestData.T0.AddMinutes(6));
        await api.DeleteAsync($"/api/chats/{withBob}/messages/{hidden}");

        var result = await SearchAsync(api, "q=запуск");

        Assert.Equal([second, first], Ids(result));
        var inGroup = result.GetProperty("items")[0].GetProperty("chat");
        Assert.Equal("group", inGroup.GetProperty("type").GetString());
        Assert.Equal("Команда", inGroup.GetProperty("title").GetString());
        var inPrivate = result.GetProperty("items")[1].GetProperty("chat");
        Assert.Equal(withBob, inPrivate.Id("chatId"));
        Assert.Equal("Bob", inPrivate.GetProperty("companionName").GetString());
        Assert.Equal("Запуск проекта в понедельник", result.GetProperty("items")[1].GetProperty("message").GetProperty("text").GetString());

        // Page by page.
        var page = await SearchAsync(api, "q=запуск&limit=1");
        Assert.Equal([second], Ids(page));
        Assert.Equal([first], Ids(await SearchAsync(api, $"q=запуск&limit=1&cursor={page.GetProperty("nextCursor").GetString()}")));

        // Left the group: its messages are out of the search.
        await api.DeleteAsync($"/api/chats/{group}/members/{alice.UserId}");
        Assert.Equal([first], Ids(await SearchAsync(api, "q=запуск")));
    }

    [Fact]
    public async Task Filters_NarrowTheSearch()
    {
        await using var factory = new ApiFactory(Db.ConnectionString, Settings);
        var alice = await factory.RegisterAsync("alice");
        using var api = factory.CreateClient(alice.Token);
        var bob = await Data.UserAsync("bob");
        var carl = await Data.UserAsync("carl");
        var withBob = await Data.PrivateChatAsync(alice.UserId, bob);
        var withCarl = await Data.PrivateChatAsync(alice.UserId, carl);
        var fromBob = await Data.MessageAsync(withBob, bob, "отчёт готов", TestData.T0.AddDays(1));
        var mine = await Data.MessageAsync(withBob, alice.UserId, "отчёт принят", TestData.T0.AddDays(2));
        var fromCarl = await Data.MessageAsync(withCarl, carl, "отчёт с фото", TestData.T0.AddDays(3));
        await using (var connection = new NpgsqlConnection(Db.ConnectionString))
            await connection.ExecuteAsync("UPDATE messages SET type = 'media' WHERE id = @fromCarl", new { fromCarl });

        Assert.Equal([mine, fromBob], Ids(await SearchAsync(api, $"q=отчёт&chatId={withBob}")));
        Assert.Equal([fromBob], Ids(await SearchAsync(api, $"q=отчёт&senderId={bob}")));
        Assert.Equal([mine], Ids(await SearchAsync(api,
            $"q=отчёт&from={TestData.T0.AddDays(2):yyyy-MM-ddTHH:mm:ssZ}&to={TestData.T0.AddDays(3):yyyy-MM-ddTHH:mm:ssZ}")));
        Assert.Equal([fromCarl], Ids(await SearchAsync(api, "q=отчёт&type=media")));
        Assert.Equal([mine, fromBob], Ids(await SearchAsync(api, "q=отчёт&type=text")));

        Assert.Equal("INVALID_QUERY", await (await api.GetAsync("/api/search/messages?q=о")).ErrorCodeAsync());
        Assert.Equal("INVALID_FILTER", await (await api.GetAsync("/api/search/messages?q=отчёт&type=voice")).ErrorCodeAsync());
        Assert.Equal("INVALID_CURSOR", await (await api.GetAsync("/api/search/messages?q=отчёт&cursor=nope")).ErrorCodeAsync());
        var foreign = await Data.PrivateChatAsync(bob, carl);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync($"/api/search/messages?q=отчёт&chatId={foreign}")).StatusCode);
    }
}
