using System.Net;
using BasicApi.IntegrationTests.Features.Push;
using BasicApi.IntegrationTests.Infrastructure;

namespace BasicApi.IntegrationTests.Features.Config;

/// <summary>GET /api/config: the limits a client checks before sending.</summary>
public class ConfigTests(PostgresFixture db) : DbTest(db)
{
    [Fact]
    public async Task Defaults()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        var alice = await factory.RegisterAsync("alice");
        using var api = factory.CreateClient(alice.Token);

        var config = await api.GetJsonAsync("/api/config");

        var messages = config.GetProperty("messages");
        Assert.Equal(4096, messages.GetProperty("maxLength").GetInt32());
        Assert.Equal(10, messages.GetProperty("maxAttachments").GetInt32());
        Assert.Equal(48, messages.GetProperty("editWindowHours").GetInt32());
        Assert.Equal(["👍", "❤️", "😂", "😮", "😢", "🙏", "👎", "🔥", "🎉"],
            messages.GetProperty("reactions").EnumerateArray().Select(r => r.GetString()));

        var media = config.GetProperty("media");
        Assert.False(media.GetProperty("enabled").GetBoolean());
        Assert.Equal(100L * 1024 * 1024, media.GetProperty("maxFileSize").GetInt64());
        Assert.Equal(20L * 1024 * 1024, media.GetProperty("maxPhotoSize").GetInt64());
        Assert.Equal(12_000_000, media.GetProperty("maxPngGifPhotoPixels").GetInt64());

        Assert.Equal(500, config.GetProperty("groups").GetProperty("maxMembers").GetInt32());
        Assert.False(config.GetProperty("push").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task FollowsTheConfiguration()
    {
        var settings = PushSubscriptionTests.PushOn();
        settings["Messages:Reactions:0"] = "👍";
        settings["Messages:Reactions:1"] = "🔥";
        settings["Messages:EditWindowHours"] = "0";
        settings["Media:MaxFileSizeMb"] = "5";
        settings["Groups:MaxMembers"] = "50";
        await using var factory = new ApiFactory(Db.ConnectionString, settings);
        var alice = await factory.RegisterAsync("alice");
        using var api = factory.CreateClient(alice.Token);

        var config = await api.GetJsonAsync("/api/config");

        Assert.Equal(["👍", "🔥"],
            config.GetProperty("messages").GetProperty("reactions").EnumerateArray().Select(r => r.GetString()));
        Assert.Equal(0, config.GetProperty("messages").GetProperty("editWindowHours").GetInt32());
        Assert.Equal(5L * 1024 * 1024, config.GetProperty("media").GetProperty("maxFileSize").GetInt64());
        Assert.Equal(50, config.GetProperty("groups").GetProperty("maxMembers").GetInt32());
        Assert.True(config.GetProperty("push").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task NeedsLogin()
    {
        await using var factory = new ApiFactory(Db.ConnectionString);
        using var api = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.GetAsync("/api/config")).StatusCode);
    }
}
