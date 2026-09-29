using System.Buffers.Binary;

namespace BasicApi.Storage.Dto;

/// <summary>
/// Chat list pagination cursor, opaque to the client (base64url): version byte 1, then the
/// last chat of the page — its last activity (UTC ticks, int64 LE) and id. The next page is the
/// chats strictly after it in (last activity desc, id desc) order.
/// </summary>
public readonly record struct ChatListCursor(DateTime LastActivityAt, Guid ChatId)
{
    private const byte Version1 = 1;
    private const int Length = 1 + 8 + 16;

    public string Encode()
    {
        Span<byte> bytes = stackalloc byte[Length];
        bytes[0] = Version1;
        BinaryPrimitives.WriteInt64LittleEndian(bytes[1..], LastActivityAt.ToUniversalTime().Ticks);
        ChatId.TryWriteBytes(bytes[9..]);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Never throws: a forged or truncated cursor is a client error (400), not a 500.</summary>
    public static bool TryDecode(string? cursor, out ChatListCursor result)
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

        if (bytes.Length != Length || bytes[0] != Version1)
            return false;
        var ticks = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(1));
        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
            return false;

        result = new ChatListCursor(new DateTime(ticks, DateTimeKind.Utc), new Guid(bytes.AsSpan(9, 16)));
        return true;
    }
}
