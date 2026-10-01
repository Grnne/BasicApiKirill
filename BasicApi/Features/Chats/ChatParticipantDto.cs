namespace BasicApi.Features.Chats;

public class ChatParticipantDto
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;

    /// <summary><c>owner</c>, <c>admin</c> or <c>member</c>; outside groups everyone is a member.</summary>
    public string Role { get; set; } = "member";

    /// <summary>The photo; null — none. Fetched by <c>POST /api/media/links</c>.</summary>
    public Guid? AvatarId { get; set; }
}
