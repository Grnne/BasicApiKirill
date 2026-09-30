using System.Buffers.Binary;

namespace BasicApi.Storage.Dto;

/// <summary>
/// Opaque (base64url) message pagination cursor: v2 is version byte 2 and the page's last seq (int64 LE).
/// A v1 cursor (32 bytes: created_at ticks, 8 zero bytes, message id) is still accepted; its seq is found by id.
/// </summary>
public readonly record struct MessageCursor
{
    private const byte Version2 = 2;
    private const int Version2Length = 9;
    private const int Version1Length = 32;

    /// <summary>The page — messages strictly earlier than this number. Null — a v1 cursor.</summary>
    public long? BeforeSeq { get; private init; }

    /// <summary>v1 cursor: id of the last message of the previous page.</summary>
    public Guid? LegacyMessageId { get; private init; }

    public static MessageCursor BeforeSeqOf(long seq) => new() { BeforeSeq = seq };

    public string Encode()
    {
        if (BeforeSeq is not { } seq)
            throw new InvalidOperationException("Only v2 cursors are issued");

        Span<byte> bytes = stackalloc byte[Version2Length];
        bytes[0] = Version2;
        BinaryPrimitives.WriteInt64LittleEndian(bytes[1..], seq);
        return ToBase64Url(bytes);
    }

    /// <summary>A v1 cursor. Only for compatibility checks.</summary>
    public static string EncodeLegacy(DateTime createdAt, Guid messageId)
    {
        Span<byte> bytes = stackalloc byte[Version1Length];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, createdAt.Ticks);
        messageId.TryWriteBytes(bytes[16..]);
        return ToBase64Url(bytes);
    }

    /// <summary>
    /// Parses a cursor from the client. Never throws: a forged or truncated
    /// cursor is a client error (400), not a 500.
    /// </summary>
    public static bool TryDecode(string? cursor, out MessageCursor result)
    {
        result = default;
        if (string.IsNullOrEmpty(cursor) || cursor.Length % 4 == 1)
            return false;

        byte[] bytes;
        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
            bytes = Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            return false;
        }

        switch (bytes.Length)
        {
            case Version2Length when bytes[0] == Version2:
                var seq = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(1));
                if (seq < 1)
                    return false;
                result = BeforeSeqOf(seq);
                return true;

            case Version1Length:
                var ticks = BinaryPrimitives.ReadInt64LittleEndian(bytes);
                if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
                    return false;
                result = new MessageCursor { LegacyMessageId = new Guid(bytes.AsSpan(16, 16)) };
                return true;

            default:
                return false;
        }
    }

    private static string ToBase64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
