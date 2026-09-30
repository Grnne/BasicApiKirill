using BasicApi.Models.Dto.Message;

namespace BasicApi.Features.Messages;

/// <summary>The chat a found message is in — enough to show it in the results.</summary>
public class SearchChatDto
{
    public Guid ChatId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Title { get; set; }
    public Guid? CompanionId { get; set; }
    public string? CompanionName { get; set; }
    public Guid? AvatarId { get; set; }
}

public class GlobalSearchHitDto
{
    public MessageDto Message { get; set; } = new();
    public SearchChatDto Chat { get; set; } = new();
}

public class GlobalSearchResponseDto
{
    /// <summary>Newest first.</summary>
    public List<GlobalSearchHitDto> Items { get; set; } = [];
    public string? NextCursor { get; set; }
    public bool HasMore { get; set; }
    public string Query { get; set; } = string.Empty;
}
