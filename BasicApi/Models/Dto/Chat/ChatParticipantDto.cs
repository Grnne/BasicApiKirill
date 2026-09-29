namespace BasicApi.Models.Dto.Chat;

public class ChatParticipantDto
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;

    /// <summary><c>owner</c>, <c>admin</c> or <c>member</c>; outside groups everyone is a member.</summary>
    public string Role { get; set; } = "member";
}
