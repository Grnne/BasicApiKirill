namespace BasicApi.Storage.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>When the user's last connection closed (or the last one opened); null — never connected.</summary>
    public DateTime? LastSeenAt { get; set; }

    /// <summary>The photo shown for the user; null — none.</summary>
    public Guid? AvatarAttachmentId { get; set; }
}