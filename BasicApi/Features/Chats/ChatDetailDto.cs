using BasicApi.Models.Dto.Chat;

namespace BasicApi.Features.Chats;

public class ChatDetailDto
{
    public Guid ChatId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? Title { get; set; }
    public List<ChatParticipantDto> Participants { get; set; } = new();

    /// <summary>The group's photo, or the companion's in a private chat; null — none.</summary>
    public Guid? AvatarId { get; set; }

    /// <summary>Who created the group; null for other chats.</summary>
    public Guid? CreatedBy { get; set; }

    /// <summary>The caller's role: <c>owner</c>, <c>admin</c> or <c>member</c>.</summary>
    public string MyRole { get; set; } = "member";

    /// <summary>What the caller may do in the group; null for other chats.</summary>
    public GroupPermissionsDto? MyPermissions { get; set; }

    /// <summary>What members may do by default in the group; null for other chats.</summary>
    public GroupPermissionsDto? MemberPermissions { get; set; }
}
