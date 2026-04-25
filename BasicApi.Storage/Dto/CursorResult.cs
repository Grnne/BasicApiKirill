namespace BasicApi.Storage.Dto;

/// <summary>
/// Wraps the result of a cursor-based query, including a
/// "one extra" record that signals whether more pages exist.
/// </summary>
public class CursorResult<T>
{
    /// <summary>Items for the current page (may be one less than fetched).</summary>
    public List<T> Items { get; init; } = [];

    /// <summary>The "one extra" record retrieved beyond the page limit, if any.</summary>
    public T? Extra { get; init; }

    /// <summary>Whether more items exist beyond this page.</summary>
    public bool HasMore => Extra is not null;
}
