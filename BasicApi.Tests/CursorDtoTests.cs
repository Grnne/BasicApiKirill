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
}

