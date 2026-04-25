using BasicApi.Storage.Dto;

namespace BasicApi.Tests;

public class CursorDtoTests
{
    [Fact]
    public void EncodeDecode_Roundtrip_PreservesValues()
    {
        // Arrange
        var createdAt = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        var id = Guid.NewGuid();
        var original = new CursorDto(createdAt, id);

        // Act
        var encoded = original.Encode();
        var decoded = CursorDto.Decode(encoded);

        // Assert
        Assert.Equal(createdAt, decoded.CreatedAt);
        Assert.Equal(id, decoded.Id);
    }

    [Fact]
    public void Encode_ProducesUrlSafeString()
    {
        // Arrange
        var cursor = new CursorDto(DateTime.UtcNow, Guid.NewGuid());

        // Act
        var encoded = cursor.Encode();

        // Assert — no URL-unsafe chars
        Assert.DoesNotContain('+', encoded);
        Assert.DoesNotContain('/', encoded);
        Assert.DoesNotContain('=', encoded);
    }

    [Fact]
    public void Decode_InvalidString_ThrowsFormatException()
    {
        // Act & Assert
        Assert.Throws<FormatException>(() => CursorDto.Decode("!!!not-base64!!!"));
    }

    [Fact]
    public void Decode_NullString_ThrowsNullReferenceException()
    {
        Assert.Throws<NullReferenceException>(() => CursorDto.Decode(null!));
    }

    [Fact]
    public void Decode_EmptyString_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CursorDto.Decode(""));
    }

    [Fact]
    public void Decode_WhitespaceString_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => CursorDto.Decode("   "));
    }

    [Fact]
    public void CompareTo_SameValues_ReturnsZero()
    {
        var dt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var id = Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA");
        var a = new CursorDto(dt, id);
        var b = new CursorDto(dt, id);

        Assert.Equal(0, a.CompareTo(b));
    }

    [Fact]
    public void CompareTo_DifferentTime_ComparesByTime()
    {
        var earlier = new CursorDto(
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Guid.NewGuid());
        var later = new CursorDto(
            new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            Guid.NewGuid());

        Assert.True(earlier.CompareTo(later) < 0);
        Assert.True(later.CompareTo(earlier) > 0);
    }

    [Fact]
    public void CompareTo_SameTimeDifferentId_ComparesById()
    {
        var dt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var idA = Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA");
        var idB = Guid.Parse("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB");
        var a = new CursorDto(dt, idA);
        var b = new CursorDto(dt, idB);

        // Compare by Guid bytes — idA < idB
        Assert.True(a.CompareTo(b) < 0);
        Assert.True(b.CompareTo(a) > 0);
    }
}
