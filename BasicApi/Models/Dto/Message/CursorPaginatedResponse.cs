namespace BasicApi.Models.Dto.Message;

/// <summary>
/// Generic cursor-based paginated response.
/// </summary>
/// <typeparam name="T">Type of items in the page.</typeparam>
public class CursorPaginatedResponse<T>
{
    /// <summary>Items in the current page.</summary>
    public List<T> Items { get; set; } = [];

    /// <summary>
    /// Cursor string to pass as <c>before</c> to fetch the next (older) page.
    /// Null when there are no more pages.
    /// </summary>
    public string? NextCursor { get; set; }

    /// <summary>Whether there are more items beyond this page.</summary>
    public bool HasMore { get; set; }
}
