namespace BasicApi.Models.Dto.Message;

/// <summary>
/// Response for full-text search in chat messages.
/// Extends <see cref="CursorPaginatedResponse{T}"/> with search-specific metadata.
/// </summary>
public class SearchMessagesResponseDto : CursorPaginatedResponse<MessageDto>
{
    /// <summary>The original search query.</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>Total number of messages matching the query (not just this page).</summary>
    public int TotalCount { get; set; }
}
