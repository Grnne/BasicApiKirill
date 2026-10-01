using BasicApi.Models.Dto.Chat;

namespace BasicApi.Features.Chats;

/// <summary>Response for chat search.</summary>
public class SearchChatsResponseDto
{
    /// <summary>Matching chat items.</summary>
    public List<ChatListItemDto> Items { get; set; } = [];

    /// <summary>The original search query.</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>Total number of matching chats.</summary>
    public int TotalCount { get; set; }
}

