using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;
using Npgsql;

namespace BasicApi.IntegrationTests.Platform;

/// <summary>
/// Time does not depend on the database session time zone: with timestamp-without-zone columns,
/// messages "drifted" by the zone offset.
/// </summary>
public class TimeZoneTests(PostgresFixture db) : DbTest(db)
{
    [Fact]
    public async Task MessageTime_IsUtc_WhenDatabaseSessionIsInAnotherTimeZone()
    {
        var moscow = new NpgsqlConnectionStringBuilder(Db.ConnectionString) { Timezone = "Europe/Moscow" }.ToString();
        await using var factory = new ApiFactory(moscow);
        var alice = await factory.RegisterAsync("alice");
        var bob = await factory.RegisterAsync("bob");
        using var client = factory.CreateClient(alice.Token);
        var chat = await Data.PrivateChatAsync(alice.UserId, bob.UserId);

        var before = DateTimeOffset.UtcNow;
        var sent = await client.PostAsJsonAsync($"/api/chats/{chat}/messages", new { text = "what time is it" });
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);

        var history = JsonDocument.Parse(await client.GetStringAsync($"/api/chats/{chat}/messages/cursor")).RootElement;
        var createdAt = history.GetProperty("items")[0].GetProperty("createdAt").GetString()!;

        // The time is marked as UTC and equals the send moment, not shifted by +3 hours.
        Assert.EndsWith("Z", createdAt);
        var stored = DateTimeOffset.Parse(createdAt);
        Assert.InRange(stored, before.AddSeconds(-5), DateTimeOffset.UtcNow.AddSeconds(5));
    }
}
