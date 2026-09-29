using System.Buffers.Binary;

namespace BasicApi.Storage.Dto;

/// <summary>
/// Курсор пагинации сообщений — непрозрачная для клиента строка (base64url).
///
/// v2 (текущий): байт версии 2 и seq последнего сообщения страницы (int64 LE).
/// v1 (до seq, без байта версии): 32 байта — ticks created_at, 8 нулевых байт, id
/// сообщения. Такие курсоры клиенты могли получить до обновления; они продолжают
/// работать — по id находится seq того же сообщения.
/// </summary>
public readonly record struct MessageCursor
{
    private const byte Version2 = 2;
    private const int Version2Length = 9;
    private const int Version1Length = 32;

    /// <summary>Страница — сообщения строго раньше этого номера. Null — курсор v1.</summary>
    public long? BeforeSeq { get; private init; }

    /// <summary>Курсор v1: id последнего сообщения предыдущей страницы.</summary>
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

    /// <summary>Курсор v1, как его выдавали до seq. Только для проверки совместимости.</summary>
    public static string EncodeLegacy(DateTime createdAt, Guid messageId)
    {
        Span<byte> bytes = stackalloc byte[Version1Length];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, createdAt.Ticks);
        messageId.TryWriteBytes(bytes[16..]);
        return ToBase64Url(bytes);
    }

    /// <summary>
    /// Разбирает курсор от клиента. Никогда не бросает: подделанный или обрезанный
    /// курсор — ошибка клиента (400), а не 500.
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
