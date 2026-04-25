namespace BasicApi.Storage.Dto;

/// <summary>
/// Result of the batched chat list query — one row per chat,
/// with all data needed to build ChatListItemDto.
/// </summary>
public class ChatListResult
{
    public Guid ChatId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? CompanionName { get; set; }
    public int UnreadCount { get; set; }
    public DateTime CreatedAt { get; set; }

    // Last message fields (nullable — chat may have no messages)
    public Guid? LastMessageId { get; set; }
    public Guid? LastMessageSenderId { get; set; }
    public string? LastMessageText { get; set; }
    public DateTime? LastMessageCreatedAt { get; set; }
    public string? LastMessageSenderName { get; set; }
}
