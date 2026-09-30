using System.Net;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;

namespace BasicApi.IntegrationTests.Features.Messages;

public class MessagesApiTests(PostgresFixture db) : DbTest(db)
{
    private async Task<(ApiFactory Factory, HttpClient Client, Guid Chat, Guid Alice, Guid Bob)> ArrangeAsync()
    {
        var factory = new ApiFactory(Db.ConnectionString);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);
        return (factory, factory.CreateClient(alice.Token), chat, alice.UserId, bob.UserId);
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static List<string> Texts(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(m => m.GetProperty("text").GetString()!)];

    [Fact]
    public async Task JumpToDate_ReturnsHistoryUpToThatMoment_IncludingThePivotMessage()
    {
        // Regression: an empty page when the anchor message was read without column mapping
        // and the cursor was built from 0001-01-01.
        var (factory, client, chat, alice, bob) = await ArrangeAsync();
        await using var _ = factory;
        using var __ = client;
        for (var h = 0; h < 4; h++)
            await Data.MessageAsync(chat, h % 2 == 0 ? alice : bob, $"h{h}", TestData.T0.AddHours(h));

        var page = await GetJsonAsync(client,
            $"/api/chats/{chat}/messages/at?date={TestData.T0.AddHours(2).AddMinutes(30):O}&limit=10");

        Assert.Equal(["h0", "h1", "h2"], Texts(page));
    }

    [Fact]
    public async Task JumpToDate_ExactTimestamp_IncludesThatMessage_AndHonoursTimezoneOffset()
    {
        var (factory, client, chat, alice, _) = await ArrangeAsync();
        await using var _f = factory;
        using var _c = client;
        await Data.MessageAsync(chat, alice, "first", TestData.T0);
        await Data.MessageAsync(chat, alice, "second", TestData.T0.AddHours(1));

        // 12:00Z stored as 15:00+03:00 is the same instant.
        var page = await GetJsonAsync(client,
            $"/api/chats/{chat}/messages/at?date={Uri.EscapeDataString("2026-01-01T15:00:00+03:00")}&limit=10");

        Assert.Equal(["first"], Texts(page));
    }

    [Fact]
    public async Task JumpToDate_AfterLastMessage_ReturnsLatestPage()
    {
        var (factory, client, chat, alice, _) = await ArrangeAsync();
        await using var _f = factory;
        using var _c = client;
        await Data.MessageAsync(chat, alice, "only", TestData.T0);

        var page = await GetJsonAsync(client,
            $"/api/chats/{chat}/messages/at?date={TestData.T0.AddDays(1):O}&limit=10");

        Assert.Equal(["only"], Texts(page));
    }

    [Theory]
    [InlineData("messages/cursor?cursor={0}")]
    [InlineData("messages/search?q=hello&cursor={0}")]
    public async Task BrokenCursor_Returns400_InsteadOf500(string pathTemplate)
    {
        var (factory, client, chat, _, _) = await ArrangeAsync();
        await using var _f = factory;
        using var _c = client;

        foreach (var cursor in new[] { "garbage!!", "abc", "AAAA" })
        {
            var response = await client.GetAsync($"/api/chats/{chat}/" + string.Format(pathTemplate, cursor));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("INVALID_CURSOR", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task LastPage_HasNullNextCursor()
    {
        var (factory, client, chat, alice, _) = await ArrangeAsync();
        await using var _f = factory;
        using var _c = client;
        for (var i = 0; i < 3; i++)
            await Data.MessageAsync(chat, alice, $"m{i}", TestData.T0.AddMinutes(i));

        var first = await GetJsonAsync(client, $"/api/chats/{chat}/messages/cursor?limit=2");
        Assert.True(first.GetProperty("hasMore").GetBoolean());
        var cursor = first.GetProperty("nextCursor").GetString();
        Assert.NotNull(cursor);

        var last = await GetJsonAsync(client, $"/api/chats/{chat}/messages/cursor?limit=2&cursor={cursor}");
        Assert.False(last.GetProperty("hasMore").GetBoolean());
        Assert.Equal(JsonValueKind.Null, last.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task Search_TotalCount_IsTheSameOnEveryPage()
    {
        // Regression: totalCount was 0 on the second and following pages.
        var (factory, client, chat, alice, _) = await ArrangeAsync();
        await using var _f = factory;
        using var _c = client;
        for (var i = 0; i < 3; i++)
            await Data.MessageAsync(chat, alice, $"hello number {i}", TestData.T0.AddMinutes(i));

        var first = await GetJsonAsync(client, $"/api/chats/{chat}/messages/search?q=hello&limit=2");
        var second = await GetJsonAsync(client,
            $"/api/chats/{chat}/messages/search?q=hello&limit=2&cursor={first.GetProperty("nextCursor").GetString()}");

        Assert.Equal(3, first.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, second.GetProperty("totalCount").GetInt32());
        Assert.Single(Texts(second));
    }
}
