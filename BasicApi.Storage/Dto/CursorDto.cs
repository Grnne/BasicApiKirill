using System.Globalization;

namespace BasicApi.Storage.Dto;

/// <summary>
/// Represents a cursor for cursor-based pagination.
/// Encodes a (CreatedAt, Id) tuple as a URL-safe Base64 string.
/// </summary>
public readonly struct CursorDto
{
    public DateTime CreatedAt { get; }
    public Guid Id { get; }

    public CursorDto(DateTime createdAt, Guid id)
    {
        CreatedAt = createdAt;
        Id = id;
    }

    /// <summary>Encodes this cursor as a URL-safe Base64 string.</summary>
    public readonly string Encode()
    {
        var ticks = CreatedAt.Ticks;
        var bytes = new byte[32];
        BitConverter.TryWriteBytes(bytes.AsSpan(0, 8), ticks);
        BitConverter.TryWriteBytes(bytes.AsSpan(8, 8), 0L);
        Id.TryWriteBytes(bytes.AsSpan(16, 16));
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>Decodes a cursor string back into a <see cref="CursorDto"/>.</summary>
    public static CursorDto Decode(string cursor)
    {
        var padded = cursor
            .Replace('-', '+')
            .Replace('_', '/');

        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        var bytes = Convert.FromBase64String(padded);
        var ticks = BitConverter.ToInt64(bytes.AsSpan(0, 8));
        var idBytes = bytes[16..32];
        return new CursorDto(new DateTime(ticks, DateTimeKind.Utc), new Guid(idBytes));
    }

    /// <summary>
    /// Decodes a cursor that came from a client. Unlike <see cref="Decode"/> it never
    /// throws: a tampered or truncated cursor is a client error (400), not a 500.
    /// </summary>
    public static bool TryDecode(string? cursor, out CursorDto result)
    {
        result = default;
        if (string.IsNullOrEmpty(cursor) || cursor.Length % 4 == 1)
            return false;

        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };

            var bytes = Convert.FromBase64String(padded);
            if (bytes.Length != 32)
                return false;

            var ticks = BitConverter.ToInt64(bytes.AsSpan(0, 8));
            if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
                return false;

            result = new CursorDto(new DateTime(ticks, DateTimeKind.Utc), new Guid(bytes.AsSpan(16, 16)));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public override readonly string ToString() => Encode();
}
