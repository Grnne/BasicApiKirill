using System.Text.Json;
using System.Text.RegularExpressions;
using BasicApi.Models.Dto.Message;

namespace BasicApi.Models;

/// <summary>
/// Formatting rules: what the server accepts in <c>entities</c>. Checked before saving,
/// so every client can render stored entities without re-checking them.
/// </summary>
public static partial class MessageEntities
{
    public const int MaxCount = 100;
    public const int MaxUrlLength = 2048;
    public const string InvalidCode = "INVALID_ENTITIES";

    public const string Mention = "mention";
    public const string Link = "link";
    public const string Pre = "pre";

    private static readonly HashSet<string> PlainTypes =
        ["bold", "italic", "underline", "strikethrough", "spoiler", "code"];

    /// <summary>A link may only lead to a web page or an email: no javascript:, data:, file: and so on.</summary>
    private static readonly HashSet<string> AllowedSchemes = ["http", "https", "mailto"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Checks entities against the text as the client sent it (<paramref name="raw"/>) and brings
    /// them to the stored, trimmed <paramref name="text"/>: offsets move left by the trimmed start,
    /// a range running into the trimmed tail is cut, a range left empty is dropped.
    /// </summary>
    /// <returns>An error message, or null if the entities are fine (then they are in <paramref name="result"/>).</returns>
    public static string? Normalize(
        IReadOnlyList<MessageEntityDto>? entities, string raw, string text, out List<MessageEntityDto> result) =>
        Normalize(entities, raw, text, raw.Length - raw.TrimStart().Length, out result);

    /// <summary>The same checks over a text that is stored as typed, without trimming (a draft).</summary>
    public static string? Validate(IReadOnlyList<MessageEntityDto>? entities, string text, out List<MessageEntityDto> result) =>
        Normalize(entities, text, text, 0, out result);

    private static string? Normalize(
        IReadOnlyList<MessageEntityDto>? entities, string raw, string text, int leadingTrim, out List<MessageEntityDto> result)
    {
        result = [];
        if (entities is null || entities.Count == 0)
            return null;
        if (entities.Count > MaxCount)
            return $"At most {MaxCount} entities are allowed";

        foreach (var entity in entities)
        {
            if (entity is null)
                return "An entity is empty";
            if (entity.Offset < 0 || entity.Length <= 0)
                return "Entity offset must be non-negative and length positive";
            if ((long)entity.Offset + entity.Length > raw.Length)
                return "Entity goes beyond the end of the text";

            var start = Math.Max(entity.Offset - leadingTrim, 0);
            var end = Math.Min(entity.Offset + entity.Length - leadingTrim, text.Length);
            if (end <= start)
                continue; // it covered only whitespace that was trimmed

            var error = CheckType(entity);
            if (error is not null)
                return error;

            result.Add(new MessageEntityDto
            {
                Type = entity.Type,
                Offset = start,
                Length = end - start,
                Url = entity.Type == Link ? entity.Url : null,
                UserId = entity.Type == Mention ? entity.UserId : null,
                Language = entity.Type == Pre ? entity.Language : null
            });
        }

        result.Sort((a, b) => a.Offset != b.Offset ? a.Offset.CompareTo(b.Offset) : b.Length.CompareTo(a.Length));
        return null;
    }

    private static string? CheckType(MessageEntityDto entity)
    {
        switch (entity.Type)
        {
            case Mention:
                return entity.UserId is null || entity.UserId == Guid.Empty ? "A mention needs userId" : null;
            case Link:
                if (string.IsNullOrWhiteSpace(entity.Url) || entity.Url.Length > MaxUrlLength)
                    return "A link needs a url of at most 2048 characters";
                return Uri.TryCreate(entity.Url, UriKind.Absolute, out var uri) && AllowedSchemes.Contains(uri.Scheme)
                    ? null
                    : "A link must be an absolute http, https or mailto URL";
            case Pre:
                return entity.Language is null || LanguagePattern().IsMatch(entity.Language)
                    ? null
                    : "Code block language must be up to 32 letters, digits or + # - . _";
            default:
                return PlainTypes.Contains(entity.Type) ? null : $"Unknown entity type '{entity.Type}'";
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9+#\-._]{1,32}$")]
    private static partial Regex LanguagePattern();

    /// <summary>Users mentioned in the entities, in order, without repeats.</summary>
    public static IReadOnlyList<Guid> MentionedUsers(IEnumerable<MessageEntityDto> entities) =>
        [.. entities.Where(e => e.Type == Mention && e.UserId is not null).Select(e => e.UserId!.Value).Distinct()];

    /// <summary>For the database: null when there is no formatting.</summary>
    public static string? Serialize(IReadOnlyList<MessageEntityDto> entities) =>
        entities.Count == 0 ? null : JsonSerializer.Serialize(entities, Json);

    public static List<MessageEntityDto> Deserialize(string? json) =>
        string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<List<MessageEntityDto>>(json, Json) ?? [];
}
