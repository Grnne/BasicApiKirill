namespace BasicApi.Models.Dto.Users;

public class SetAvatarDto
{
    /// <summary>A completed photo upload of the caller.</summary>
    public Guid AttachmentId { get; set; }
}

/// <summary>
/// <c>UserUpdated</c>: a user's public profile changed — to the user's devices and everyone who
/// shares a chat with them.
/// </summary>
public class UserUpdatedDto
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;

    /// <summary>The photo; null — none. Fetched by <c>POST /api/media/links</c>.</summary>
    public Guid? AvatarId { get; set; }
}
