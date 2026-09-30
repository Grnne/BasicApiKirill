using System.Text.Json.Serialization;

namespace BasicApi.Models.Dto.Message;

/// <summary>
/// A piece of formatting over the message text. <see cref="Offset"/> and <see cref="Length"/>
/// are in UTF-16 code units — the same as string indices in JavaScript, Java, C# and Swift's
/// <c>utf16</c> view. A record: two lists of entities compare by value.
/// </summary>
public sealed record MessageEntityDto
{
    /// <summary>
    /// <c>bold</c>, <c>italic</c>, <c>underline</c>, <c>strikethrough</c>, <c>spoiler</c>,
    /// <c>code</c>, <c>pre</c> (a code block, with <see cref="Language"/>), <c>link</c>
    /// (with <see cref="Url"/>), <c>mention</c> (with <see cref="UserId"/>).
    /// </summary>
    public string Type { get; set; } = string.Empty;

    public int Offset { get; set; }
    public int Length { get; set; }

    /// <summary><c>link</c>: http, https or mailto.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; set; }

    /// <summary><c>mention</c>: the mentioned member of the chat.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? UserId { get; set; }

    /// <summary><c>pre</c>: language for highlighting, optional.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Language { get; set; }
}
