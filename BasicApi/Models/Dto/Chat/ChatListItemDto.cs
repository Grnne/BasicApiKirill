using BasicApi.Models.Dto.Message;

namespace BasicApi.Models.Dto.Chat;

// ========== Models/Dto/Chat/ChatListItemDto.cs ==========
public class ChatListItemDto
{
    public Guid ChatId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? CompanionName { get; set; }
    public MessageDto? LastMessage { get; set; }
    public int UnreadCount { get; set; }
    public DateTime LastActivityAt { get; set; }
}
