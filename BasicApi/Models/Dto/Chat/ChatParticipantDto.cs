namespace BasicApi.Models.Dto.Chat;

public class ChatParticipantDto
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
}
