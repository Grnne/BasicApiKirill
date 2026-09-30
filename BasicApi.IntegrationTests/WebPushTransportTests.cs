using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BasicApi.IntegrationTests.Infrastructure;
using BasicApi.Services.Push;
using BasicApi.Storage.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BasicApi.IntegrationTests;

/// <summary>
/// What goes to a push service, byte for byte: the browser must be able to decrypt it with its keys
/// (RFC 8291) and the service must accept the server's signature (RFC 8292).
/// </summary>
public class WebPushTransportTests
{
    private sealed class PushServiceStub(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public byte[] Body { get; private set; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            Body = await request.Content!.ReadAsByteArrayAsync(ct);
            return new HttpResponseMessage(status);
        }
    }

    private static readonly (string Public, string Private) Keys = VapidKeys.Generate();

    private static WebPushTransport Transport(HttpMessageHandler handler) => new(new HttpClient(handler),
        Options.Create(new PushOptions
        {
            VapidPublicKey = Keys.Public,
            VapidPrivateKey = Keys.Private,
            Subject = "mailto:admin@test.local",
            TtlSeconds = 3600
        }),
        NullLogger<WebPushTransport>.Instance);

    private static DevicePush Target(PushTestDevice device) =>
        new(device.Endpoint, device.P256dh, Base64Url.EncodeToString(device.AuthSecret));

    [Fact]
    public async Task Notification_IsEncryptedForTheBrowser_AndSignedByTheServer()
    {
        using var device = new PushTestDevice();
        var service = new PushServiceStub(HttpStatusCode.Created);
        using var transport = Transport(service);
        const string payload = "{\"kind\":\"message\",\"text\":\"Привет\"}";

        Assert.Equal(PushDelivery.Sent, await transport.SendAsync(Target(device), payload, "0123456789abcdef0123456789abcdef"));

        var request = service.Request!;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(device.Endpoint, request.RequestUri!.ToString());
        Assert.Equal("aes128gcm", Assert.Single(request.Content!.Headers.ContentEncoding));
        Assert.Equal("3600", Assert.Single(request.Headers.GetValues("TTL")));
        Assert.Equal("0123456789abcdef0123456789abcdef", Assert.Single(request.Headers.GetValues("Topic")));
        Assert.Equal("high", Assert.Single(request.Headers.GetValues("Urgency")));

        // Only the browser's keys open it.
        Assert.Equal(payload, device.Decrypt(service.Body));
        Assert.Equal(-1, service.Body.AsSpan().IndexOf("Привет"u8));

        // Authorization: vapid t=<JWT>, k=<the server's public key>
        var auth = request.Headers.Authorization!;
        Assert.Equal("vapid", auth.Scheme);
        var parts = auth.Parameter!.Split(',', StringSplitOptions.TrimEntries)
            .ToDictionary(p => p[..p.IndexOf('=')], p => p[(p.IndexOf('=') + 1)..]);
        Assert.Equal(Keys.Public, parts["k"]);
        var jwt = parts["t"].Split('.');
        var claims = JsonDocument.Parse(Base64Url.DecodeFromChars(jwt[1])).RootElement;
        Assert.Equal("https://fcm.googleapis.com", claims.GetProperty("aud").GetString());
        Assert.Equal("mailto:admin@test.local", claims.GetProperty("sub").GetString());
        var expires = DateTimeOffset.FromUnixTimeSeconds(claims.GetProperty("exp").GetInt64());
        Assert.InRange(expires, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24)); // RFC 8292: at most 24 h

        var q = Base64Url.DecodeFromChars(Keys.Public);
        using var serverKey = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = q[1..33], Y = q[33..] }
        });
        Assert.True(serverKey.VerifyData(Encoding.ASCII.GetBytes($"{jwt[0]}.{jwt[1]}"),
            Base64Url.DecodeFromChars(jwt[2]), HashAlgorithmName.SHA256));
    }

    [Theory]
    [InlineData(HttpStatusCode.Gone, PushDelivery.Gone)]
    [InlineData(HttpStatusCode.NotFound, PushDelivery.Gone)]
    [InlineData(HttpStatusCode.TooManyRequests, PushDelivery.Failed)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, PushDelivery.Failed)]
    [InlineData(HttpStatusCode.InternalServerError, PushDelivery.Failed)]
    public async Task PushServiceAnswer_TellsWhatToDoWithTheSubscription(HttpStatusCode status, PushDelivery expected)
    {
        using var device = new PushTestDevice();
        using var transport = Transport(new PushServiceStub(status));

        Assert.Equal(expected, await transport.SendAsync(Target(device), "{}", "topic"));
    }
}
