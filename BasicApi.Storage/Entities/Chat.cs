namespace BasicApi.Storage.Entities;

public class Chat
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? Title { get; set; }
    public string Type { get; set; } = ChatTypes.Private;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Who created the group; null for other chats and when that user is gone.</summary>
    public Guid? CreatedBy { get; set; }

    /// <summary>When the group's title or settings last changed.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Group settings as JSON (the default permissions of members); null — the defaults.</summary>
    public string? SettingsJson { get; set; }

    /// <summary>The group's photo; null — none.</summary>
    public Guid? AvatarAttachmentId { get; set; }
}

public static class ChatTypes
{
    public const string Private = "private";
    public const string Group = "group";
    public const string Saved = "saved";
}

/// <summary>A member's role in a group; in other chats everyone is a member.</summary>
public static class ChatRoles
{
    public const string Owner = "owner";
    public const string Admin = "admin";
    public const string Member = "member";

    public static bool IsValid(string? role) => role is Owner or Admin or Member;
}
