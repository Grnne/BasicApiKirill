namespace BasicApi.Models;

/// <summary>
/// Правила текста сообщения — одни для хаба и для будущей REST-отправки.
/// </summary>
public static class MessageText
{
    /// <summary>Как у крупных мессенджеров: длиннее — это уже документ, а не сообщение.</summary>
    public const int MaxLength = 4096;

    public const string EmptyCode = "MESSAGE_EMPTY";
    public const string TooLongCode = "MESSAGE_TOO_LONG";

    /// <summary>
    /// Обрезает пробелы по краям и проверяет длину.
    /// </summary>
    /// <returns>Код ошибки или null, если текст годится (тогда он в <paramref name="text"/>).</returns>
    public static string? Normalize(string? raw, out string text)
    {
        text = raw?.Trim() ?? string.Empty;

        if (text.Length == 0)
            return EmptyCode;

        return text.Length > MaxLength ? TooLongCode : null;
    }
}
