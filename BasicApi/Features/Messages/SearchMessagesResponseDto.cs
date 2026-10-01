using BasicApi.Models.Dto.Message;

namespace BasicApi.Features.Messages;

/// <summary>A page of full-text search results in one chat.</summary>
public class SearchMessagesResponseDto : CursorPaginatedResponse<MessageDto>
{
    /// <summary>The original search query.</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>Total number of messages matching the query (not just this page).</summary>
    public int TotalCount { get; set; }
}
