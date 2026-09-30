using BasicApi.Services.Push;

namespace BasicApi.Tests.Services;

public class PushOptionsTests
{
    [Fact]
    public void GeneratedKeys_AreAPair_InTheFormBrowsersTake()
    {
        var (publicKey, privateKey) = VapidKeys.Generate();

        Assert.True(VapidKeys.IsValidPair(publicKey, privateKey));
        Assert.Equal(87, publicKey.Length); // 65 bytes, base64url without padding
        Assert.Equal(43, privateKey.Length);
        Assert.DoesNotContain('=', publicKey);
    }

    [Fact]
    public void KeysOfDifferentPairs_OrNotKeysAtAll_AreRejected()
    {
        var (publicKey, privateKey) = VapidKeys.Generate();
        var (otherPublic, otherPrivate) = VapidKeys.Generate();

        Assert.False(VapidKeys.IsValidPair(otherPublic, privateKey));
        Assert.False(VapidKeys.IsValidPair(publicKey, otherPrivate));
        Assert.False(VapidKeys.IsValidPair("not base64 !", privateKey));
        Assert.False(VapidKeys.IsValidPair(publicKey[..40], privateKey));
        Assert.False(VapidKeys.IsValidPair(publicKey, privateKey + "AA"));
    }

    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/abc", true)]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/abc", true)]
    [InlineData("https://wns2-am3p.notify.windows.com/w/?token=abc", true)]
    [InlineData("https://web.push.apple.com/abc", true)]
    [InlineData("https://FCM.googleapis.com/fcm/send/abc", true)]
    [InlineData("http://fcm.googleapis.com/fcm/send/abc", false)] // not https
    [InlineData("https://fcm.googleapis.com:8443/fcm/send/abc", false)] // another port
    [InlineData("https://fcm.googleapis.com.evil.example/abc", false)]
    [InlineData("https://evilfcm.googleapis.com/abc", false)]
    [InlineData("https://push.services.mozilla.com.evil.example/abc", false)]
    [InlineData("https://fcm.googleapis.com@evil.example/abc", false)] // user info: the host is evil.example
    [InlineData("https://user@fcm.googleapis.com/abc", false)]
    [InlineData("https://localhost/abc", false)]
    [InlineData("https://127.0.0.1/abc", false)]
    [InlineData("https://[::1]/abc", false)]
    [InlineData("https://169.254.169.254/latest/meta-data", false)]
    public void Endpoint_OnlyHttpsOfKnownPushServices(string endpoint, bool allowed)
    {
        Assert.Equal(allowed, new PushOptions().IsAllowedEndpoint(new Uri(endpoint)));
    }

    [Fact]
    public void AllowedHosts_AreConfigurable()
    {
        var options = new PushOptions { AllowedHosts = "push.example.org, *.push.test" };

        Assert.True(options.IsAllowedEndpoint(new Uri("https://push.example.org/x")));
        Assert.True(options.IsAllowedEndpoint(new Uri("https://a.push.test/x")));
        Assert.False(options.IsAllowedEndpoint(new Uri("https://push.test/x"))); // *. needs a subdomain
        Assert.False(options.IsAllowedEndpoint(new Uri("https://fcm.googleapis.com/x")));
    }
}
