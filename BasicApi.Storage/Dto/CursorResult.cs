namespace BasicApi.Storage.Dto;

/// <summary>A page of a cursor query plus the one extra row fetched beyond the limit that tells whether more pages exist.</summary>
public class CursorResult<T>
{
    public List<T> Items { get; init; } = [];

    public T? Extra { get; init; }

    public bool HasMore => Extra is not null;
}
