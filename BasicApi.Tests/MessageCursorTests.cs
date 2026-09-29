using BasicApi.Storage.Dto;

namespace BasicApi.Tests;

public class MessageCursorTests
{
    [Fact]
    public void Seq_Roundtrip()
    {
        var encoded = MessageCursor.BeforeSeqOf(123_456_789_012).Encode();

        Assert.True(MessageCursor.TryDecode(encoded, out var decoded));
        Assert.Equal(123_456_789_012, decoded.BeforeSeq);
        Assert.Null(decoded.LegacyMessageId);
    }

    [Fact]
    public void Cursor_IsUrlSafe()
    {
        for (var seq = 1L; seq < 5000; seq += 37)
            Assert.DoesNotMatch("[+/=]", MessageCursor.BeforeSeqOf(seq).Encode());
    }

    [Fact]
    public void OldFormat_IsStillAccepted_AsAPointerToTheMessage()
    {
        // Курсоры, выданные до seq, остаются у клиентов после обновления.
        var id = Guid.NewGuid();
        var legacy = MessageCursor.EncodeLegacy(new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc), id);

        Assert.True(MessageCursor.TryDecode(legacy, out var decoded));
        Assert.Null(decoded.BeforeSeq);
        Assert.Equal(id, decoded.LegacyMessageId);
    }

    [Theory]
    [InlineData("garbage!!")]
    [InlineData("abc")]
    [InlineData("AAAA")]
    [InlineData("")]
    [InlineData(null)]
    public void Malformed_IsRejected(string? cursor) =>
        Assert.False(MessageCursor.TryDecode(cursor, out _));

    [Fact]
    public void UnknownVersionOrNonPositiveSeq_IsRejected()
    {
        static string Raw(byte version, long seq)
        {
            var bytes = new byte[9];
            bytes[0] = version;
            BitConverter.TryWriteBytes(bytes.AsSpan(1), seq);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        Assert.True(MessageCursor.TryDecode(Raw(2, 5), out _));
        Assert.False(MessageCursor.TryDecode(Raw(3, 5), out _));
        Assert.False(MessageCursor.TryDecode(Raw(2, 0), out _));
        Assert.False(MessageCursor.TryDecode(Raw(2, -1), out _));
    }
}
