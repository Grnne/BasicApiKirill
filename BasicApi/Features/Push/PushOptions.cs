using System.Buffers.Text;
using System.Security.Cryptography;

namespace BasicApi.Features.Push;

/// <summary>
/// Push notifications (WebPush), section <c>Push</c>. Without the VAPID keys push is off: clients
/// learn it from <c>GET /api/push/config</c>, subscribing answers 503 <c>PUSH_UNAVAILABLE</c>.
/// </summary>
public sealed class PushOptions
{
    public const string Section = "Push";

    public const string DefaultAllowedHosts =
        "fcm.googleapis.com,*.push.services.mozilla.com,*.notify.windows.com,*.push.apple.com";

    /// <summary>The public VAPID key, base64url: browsers tie subscriptions to it — do not change it.</summary>
    public string? VapidPublicKey { get; set; }

    public string? VapidPrivateKey { get; set; }

    /// <summary>How push services reach the operator: <c>mailto:</c> or <c>https:</c>.</summary>
    public string? Subject { get; set; }

    /// <summary>
    /// The push services the server sends to, comma-separated; <c>*.example.com</c> — any
    /// subdomain. The endpoint comes from the client, so without the list the server would post to
    /// any address it is given, internal ones included.
    /// </summary>
    public string AllowedHosts { get; set; } = DefaultAllowedHosts;

    /// <summary>How long a push service keeps a notification for a device that is offline.</summary>
    public int TtlSeconds { get; set; } = 24 * 60 * 60;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(VapidPublicKey) && !string.IsNullOrWhiteSpace(VapidPrivateKey);

    /// <summary>An https address of an allowed push service on the standard port.</summary>
    public bool IsAllowedEndpoint(Uri endpoint)
    {
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps || !endpoint.IsDefaultPort
            || endpoint.UserInfo.Length > 0 || endpoint.HostNameType != UriHostNameType.Dns)
            return false;

        var host = endpoint.IdnHost;
        foreach (var allowed in AllowedHosts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (allowed.StartsWith("*.", StringComparison.Ordinal)
                    ? host.EndsWith(allowed[1..], StringComparison.OrdinalIgnoreCase)
                    : host.Equals(allowed, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}

/// <summary>
/// VAPID keys (RFC 8292): a P-256 pair the server signs its requests to push services with.
/// Both are base64url, as browsers take them: the public key is the uncompressed point (65 bytes),
/// the private one the scalar (32 bytes).
/// </summary>
public static class VapidKeys
{
    public static (string PublicKey, string PrivateKey) Generate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(includePrivateParameters: true);
        return (Base64Url.EncodeToString([0x04, .. parameters.Q.X!, .. parameters.Q.Y!]),
            Base64Url.EncodeToString(parameters.D!));
    }

    public static bool IsValidPair(string publicKey, string privateKey)
    {
        try
        {
            var q = Base64Url.DecodeFromChars(publicKey);
            var d = Base64Url.DecodeFromChars(privateKey);
            if (q.Length != 65 || q[0] != 0x04 || d.Length != 32)
                return false;

            var point = new ECPoint { X = q[1..33], Y = q[33..] };
            using var pair = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = point, D = d });
            using var publicOnly = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = point });
            // An import may take a private key that is not the public one's: a signature tells.
            var data = "vapid"u8.ToArray();
            return publicOnly.VerifyData(data, pair.SignData(data, HashAlgorithmName.SHA256), HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }
}
