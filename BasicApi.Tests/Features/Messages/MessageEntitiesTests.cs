using BasicApi.Models;
using BasicApi.Models.Dto.Message;

namespace BasicApi.Tests.Features.Messages;

/// <summary>What the server accepts as formatting (D6, R31).</summary>
public class MessageEntitiesTests
{
    private static MessageEntityDto E(string type, int offset, int length, string? url = null, Guid? userId = null, string? language = null) =>
        new() { Type = type, Offset = offset, Length = length, Url = url, UserId = userId, Language = language };

    private static List<MessageEntityDto> Valid(string raw, params MessageEntityDto[] entities)
    {
        var error = MessageEntities.Normalize(entities, raw, raw.Trim(), out var result);
        Assert.Null(error);
        return result;
    }

    private static string Invalid(string raw, params MessageEntityDto[] entities)
    {
        var error = MessageEntities.Normalize(entities, raw, raw.Trim(), out _);
        Assert.NotNull(error);
        return error;
    }

    [Fact]
    public void NoEntities_IsPlainText()
    {
        Assert.Empty(Valid("hello"));
        Assert.Null(MessageEntities.Normalize(null, "hello", "hello", out var result));
        Assert.Empty(result);
        Assert.Null(MessageEntities.Serialize(result));
    }

    [Theory]
    [InlineData("bold")]
    [InlineData("italic")]
    [InlineData("underline")]
    [InlineData("strikethrough")]
    [InlineData("spoiler")]
    [InlineData("code")]
    [InlineData("pre")]
    public void PlainTypes_AreAccepted(string type)
    {
        var entity = Assert.Single(Valid("hello world", E(type, 6, 5)));

        Assert.Equal((type, 6, 5), (entity.Type, entity.Offset, entity.Length));
    }

    [Fact]
    public void Offsets_FollowTheTrimmedText()
    {
        // "  hi there  " is stored as "hi there": everything moves two to the left,
        // a range running into the trimmed tail is cut, one over whitespace only is dropped.
        var result = Valid("  hi there  ", E("bold", 2, 2), E("italic", 5, 7), E("code", 0, 2));

        Assert.Equal([E("bold", 0, 2), E("italic", 3, 5)], result);
    }

    [Fact]
    public void Entities_AreSortedByOffset()
    {
        var result = Valid("abcdef", E("bold", 3, 2), E("italic", 0, 6), E("code", 0, 1));

        Assert.Equal([E("italic", 0, 6), E("code", 0, 1), E("bold", 3, 2)], result);
    }

    [Fact]
    public void Utf16Offsets_CountSurrogatePairsAsTwo()
    {
        // "😀 ok": the emoji is two UTF-16 units, as in JavaScript.
        var result = Valid("😀 ok", E("bold", 3, 2));

        Assert.Equal("ok", "😀 ok".Substring(result[0].Offset, result[0].Length));
    }

    [Theory]
    [InlineData(-1, 2)]
    [InlineData(0, 0)]
    [InlineData(0, -3)]
    [InlineData(3, 3)]
    [InlineData(int.MaxValue, 1)]
    public void OutOfRange_IsRejected(int offset, int length) =>
        Invalid("hello", E("bold", offset, length));

    [Fact]
    public void UnknownType_IsRejected() =>
        Assert.Contains("Unknown entity type", Invalid("hello", E("blink", 0, 5)));

    [Fact]
    public void TooMany_AreRejected() =>
        Invalid("hello", [.. Enumerable.Range(0, MessageEntities.MaxCount + 1).Select(_ => E("bold", 0, 1))]);

    [Theory]
    [InlineData("https://example.com/a?b=c")]
    [InlineData("http://example.com")]
    [InlineData("mailto:someone@example.com")]
    public void Links_ToWebAndMail_AreAccepted(string url) =>
        Assert.Equal(url, Assert.Single(Valid("click", E("link", 0, 5, url: url))).Url);

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("/relative/path")]
    [InlineData("example.com")]
    [InlineData("")]
    [InlineData(null)]
    public void Links_ToAnythingElse_AreRejected(string? url) =>
        Invalid("click", E("link", 0, 5, url: url));

    [Fact]
    public void Link_TooLong_IsRejected() =>
        Invalid("click", E("link", 0, 5, url: "https://example.com/" + new string('a', MessageEntities.MaxUrlLength)));

    [Fact]
    public void Mention_NeedsAUser()
    {
        Invalid("@bob", E("mention", 0, 4));
        Invalid("@bob", E("mention", 0, 4, userId: Guid.Empty));

        var bob = Guid.NewGuid();
        Assert.Equal([bob], MessageEntities.MentionedUsers(Valid("@bob @bob", E("mention", 0, 4, userId: bob), E("mention", 5, 4, userId: bob))));
    }

    [Theory]
    [InlineData("csharp", true)]
    [InlineData("c++", true)]
    [InlineData("objective-c", true)]
    [InlineData(null, true)]
    [InlineData("rm -rf /", false)]
    [InlineData("<script>", false)]
    public void CodeBlockLanguage_IsAWord(string? language, bool ok)
    {
        var error = MessageEntities.Normalize([E("pre", 0, 4, language: language)], "code", "code", out _);

        Assert.Equal(ok, error is null);
    }

    [Fact]
    public void FieldsThatDoNotBelongToTheType_AreDropped()
    {
        var entity = Assert.Single(Valid("hello", E("bold", 0, 5, url: "https://x.y", userId: Guid.NewGuid(), language: "js")));

        Assert.Null(entity.Url);
        Assert.Null(entity.UserId);
        Assert.Null(entity.Language);
    }

    [Fact]
    public void Serialization_RoundTrips()
    {
        var entities = Valid("see @bob", E("link", 0, 3, url: "https://x.y"), E("mention", 4, 4, userId: Guid.NewGuid()));

        Assert.Equal(entities, MessageEntities.Deserialize(MessageEntities.Serialize(entities)));
    }
}
