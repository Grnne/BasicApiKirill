using BasicApi.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;

namespace BasicApi.Tests.Extensions;

public class ConfigurationValidationTests
{
    private const string StrongKey = "0123456789abcdef0123456789abcdef-strong";
    private const string OldPlaceholder = "your-super-secret-key-with-at-least-32-characters-long";

    private static IConfiguration Config(string? key, string? connection = "Host=db",
        string? issuer = "ChatApi", string? audience = "ChatClient") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = key,
            ["Jwt:Issuer"] = issuer,
            ["Jwt:Audience"] = audience,
            ["ConnectionStrings:DefaultConnection"] = connection
        }).Build();

    private static IHostEnvironment Env(string name)
    {
        var env = new Mock<IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns(name);
        return env.Object;
    }

    [Fact]
    public void ValidProductionConfig_Passes() =>
        ConfigurationValidation.Validate(Config(StrongKey), Env("Production"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingJwtKey_Fails_InAnyEnvironment(string? key)
    {
        Assert.Throws<InvalidOperationException>(() =>
            ConfigurationValidation.Validate(Config(key), Env("Development")));
        Assert.Throws<InvalidOperationException>(() =>
            ConfigurationValidation.Validate(Config(key), Env("Production")));
    }

    [Fact]
    public void ShortJwtKey_Fails_InAnyEnvironment()
    {
        // HS256 requires a key of at least 256 bits; no token can be signed with a shorter one.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConfigurationValidation.Validate(Config("too-short"), Env("Development")));
        Assert.Contains("Jwt:Key", ex.Message);
    }

    [Theory]
    [InlineData(OldPlaceholder)]
    [InlineData("CHANGE_ME__openssl_rand_-base64_48")]
    [InlineData("dev-only-signing-key-do-not-use-in-production-0123456789")]
    public void PlaceholderJwtKey_FailsOutsideDevelopment(string key)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConfigurationValidation.Validate(Config(key), Env("Production")));
        Assert.Contains("placeholder", ex.Message);

        ConfigurationValidation.Validate(Config(key), Env("Development"));
    }

    [Fact]
    public void MissingConnectionString_Fails()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConfigurationValidation.Validate(Config(StrongKey, connection: null), Env("Production")));
        Assert.Contains("DefaultConnection", ex.Message);
    }

    [Fact]
    public void MissingIssuerOrAudience_Fails()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ConfigurationValidation.Validate(Config(StrongKey, issuer: ""), Env("Production")));
        Assert.Throws<InvalidOperationException>(() =>
            ConfigurationValidation.Validate(Config(StrongKey, audience: null), Env("Production")));
    }

    [Fact]
    public void AllProblems_AreReportedAtOnce()
    {
        // So the config does not have to be fixed one error per restart.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConfigurationValidation.Validate(Config(null, connection: null), Env("Production")));
        Assert.Contains("Jwt:Key", ex.Message);
        Assert.Contains("DefaultConnection", ex.Message);
    }

    private static IConfiguration WithStorage(string? accessKey, string? secretKey) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = StrongKey,
            ["Jwt:Issuer"] = "ChatApi",
            ["Jwt:Audience"] = "ChatClient",
            ["ConnectionStrings:DefaultConnection"] = "Host=db",
            ["Storage:Endpoint"] = "http://seaweedfs:8333",
            ["Storage:AccessKey"] = accessKey,
            ["Storage:SecretKey"] = secretKey
        }).Build();

    [Fact]
    public void Storage_NeedsItsKeys_AndNotThePlaceholder()
    {
        ConfigurationValidation.Validate(WithStorage("chat-media", "a-real-secret"), Env("Production"));
        Assert.Throws<InvalidOperationException>(() =>
            ConfigurationValidation.Validate(WithStorage(null, "a-real-secret"), Env("Development")));
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConfigurationValidation.Validate(WithStorage("chat-media", "CHANGE_ME__openssl_rand_-base64_24"), Env("Production")));
        Assert.Contains("Storage:SecretKey", ex.Message);
    }

    private static IConfiguration WithPush(string? publicKey, string? privateKey, string? subject = "mailto:admin@example.com") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = StrongKey,
            ["Jwt:Issuer"] = "ChatApi",
            ["Jwt:Audience"] = "ChatClient",
            ["ConnectionStrings:DefaultConnection"] = "Host=db",
            ["Push:VapidPublicKey"] = publicKey,
            ["Push:VapidPrivateKey"] = privateKey,
            ["Push:Subject"] = subject
        }).Build();

    [Fact]
    public void Push_NeedsAMatchingKeyPair_AndASubject()
    {
        var (publicKey, privateKey) = BasicApi.Services.Push.VapidKeys.Generate();
        var (otherPublic, _) = BasicApi.Services.Push.VapidKeys.Generate();

        ConfigurationValidation.Validate(WithPush(publicKey, privateKey), Env("Production"));
        ConfigurationValidation.Validate(WithPush(null, null, subject: null), Env("Production")); // push off
        Assert.Contains("both", Assert.Throws<InvalidOperationException>(() =>
            ConfigurationValidation.Validate(WithPush(publicKey, null), Env("Production"))).Message);
        Assert.Contains("key pair", Assert.Throws<InvalidOperationException>(() =>
            ConfigurationValidation.Validate(WithPush(otherPublic, privateKey), Env("Production"))).Message);
        Assert.Contains("Push:Subject", Assert.Throws<InvalidOperationException>(() =>
            ConfigurationValidation.Validate(WithPush(publicKey, privateKey, subject: "admin@example.com"), Env("Production"))).Message);
    }
}
