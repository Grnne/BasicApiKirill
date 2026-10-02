namespace BasicApi.Storage.Repositories;

/// <summary>Text the user searches for, as an ILIKE pattern: their % and _ are characters, not wildcards.</summary>
internal static class Like
{
    public static string Contains(string text) =>
        "%" + text.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";
}
