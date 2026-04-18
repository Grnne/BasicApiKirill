namespace BasicApi.Models.Dto.Chat;

// ========== Models/Dto/Chat/ChatDetailDto.cs ==========
public class ChatDetailDto
{
    public Guid ChatId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Title { get; set; }
    public List<ChatParticipantDto> Participants { get; set; } = new();
}
