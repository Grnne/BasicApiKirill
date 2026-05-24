using BasicApi.Models.Dto.Message;

namespace BasicApi.Models.Dto.Chat;

/// <summary>
/// Response for chat search (by title for group chats or by companion name/username for private chats).
/// </summary>
public class SearchChatsResponseDto
{
    /// <summary>Matching chat items.</summary>
    public List<ChatListItemDto> Items { get; set; } = [];

    /// <summary>The original search query.</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>Total number of matching chats.</summary>
    public int TotalCount { get; set; }
}

