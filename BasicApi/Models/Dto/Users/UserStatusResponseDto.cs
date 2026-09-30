namespace BasicApi.Models.Dto.Users;

public class UserStatusDto
{
    public Guid UserId { get; set; }
    public bool IsOnline { get; set; }

    /// <summary>
    /// When the user was last online, if offline and they show it to the caller; null otherwise
    /// (online, hidden by their privacy settings, or never seen).
    /// </summary>
    public DateTime? LastSeenAt { get; set; }
}

public class UserStatusResponseDto
{
    public List<UserStatusDto> Items { get; set; } = [];
}
