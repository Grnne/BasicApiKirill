using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

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

    /// <summary>
    /// Decrypts a notification body as the browser does (RFC 8291 over RFC 8188 <c>aes128gcm</c>,
    /// one record) — written from the RFCs, independently of the server's library.
    /// </summary>
    public string Decrypt(byte[] body)
    {
        var salt = body[..16];
        var recordSize = BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(16, 4));
        int idLength = body[20];
        var serverPublic = body[21..(21 + idLength)];
        var cipherText = body[(21 + idLength)..];
        Assert.Equal(65, idLength);
        Assert.True(cipherText.Length <= recordSize, "one record expected");

        using var server = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = serverPublic[1..33], Y = serverPublic[33..] }
        });
        var sharedSecret = Key.DeriveRawSecretAgreement(server.PublicKey);
        var ownPublic = Base64Url.DecodeFromChars(P256dh);

        // IKM = HKDF(salt = auth secret, ikm = ECDH secret, info = "WebPush: info" 0 ua_public as_public)
        byte[] keyInfo = [.. Encoding.ASCII.GetBytes("WebPush: info\0"), .. ownPublic, .. serverPublic];
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 32, AuthSecret, keyInfo);
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var contentKey = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));

        var plain = new byte[cipherText.Length - 16];
        using (var aes = new AesGcm(contentKey, 16))
            aes.Decrypt(nonce, cipherText[..^16], cipherText[^16..], plain);

        // The last record ends with 0x02, then optional zero padding.
        var end = plain.Length - 1;
        while (plain[end] == 0) end--;
        Assert.Equal(2, plain[end]);
        return Encoding.UTF8.GetString(plain, 0, end);
    }

    public void Dispose() => Key.Dispose();
}
