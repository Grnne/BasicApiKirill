using System.Buffers.Text;
using System.Security.Cryptography;

namespace BasicApi.IntegrationTests.Infrastructure;

/// <summary>
/// A browser's side of a push subscription: its P-256 key pair and auth secret, and an endpoint
/// at a push service. Nothing is sent anywhere in tests — the server's push client is replaced.
/// </summary>
public sealed class PushTestDevice : IDisposable
{
    public ECDiffieHellman Key { get; } = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
    public byte[] AuthSecret { get; } = RandomNumberGenerator.GetBytes(16);
    public string Endpoint { get; } = $"https://fcm.googleapis.com/fcm/send/{Guid.NewGuid():N}";

    public string P256dh
    {
        get
        {
            var q = Key.ExportParameters(false).Q;
            return Base64Url.EncodeToString([0x04, .. q.X!, .. q.Y!]);
        }
    }

    /// <summary>What the browser sends: <c>PushSubscription.toJSON()</c>.</summary>
    public object Subscription => new
    {
        endpoint = Endpoint,
        expirationTime = (long?)null,
        keys = new { p256dh = P256dh, auth = Base64Url.EncodeToString(AuthSecret) }
    };

    public void Dispose() => Key.Dispose();
}
