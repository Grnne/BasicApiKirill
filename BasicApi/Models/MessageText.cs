namespace BasicApi.Models;

/// <summary>
/// Message text rules — the same for the hub and for the future REST send.
/// </summary>
public static class MessageText
{
    /// <summary>Like the major messengers: anything longer is a document, not a message.</summary>
    public const int MaxLength = 4096;

    public const string EmptyCode = "MESSAGE_EMPTY";
    public const string TooLongCode = "MESSAGE_TOO_LONG";

    /// <summary>
    /// Trims whitespace at the edges and checks the length.
    /// </summary>
    /// <returns>An error code, or null if the text is fine (then it is in <paramref name="text"/>).</returns>
    public static string? Normalize(string? raw, out string text)
    {
        text = raw?.Trim() ?? string.Empty;

        if (text.Length == 0)
            return EmptyCode;

        return text.Length > MaxLength ? TooLongCode : null;
    }
}
