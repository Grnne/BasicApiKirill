namespace BasicApi.Storage.Dto;

/// <summary>A member of a chat with what decides their rights: the role, their overrides and the chat's settings.</summary>
public class ChatMember
{
    public Guid ChatId { get; set; }
    public Guid UserId { get; set; }
    public string ChatType { get; set; } = string.Empty;
    public string Role { get; set; } = Entities.ChatRoles.Member;

    /// <summary>The member's own permission overrides as JSON; null — none.</summary>
    public string? PermissionsJson { get; set; }

    /// <summary>The chat's settings as JSON; null — the defaults.</summary>
    public string? SettingsJson { get; set; }

    public DateTime JoinedAt { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public Guid? AvatarId { get; set; }
    public string Username { get; set; } = string.Empty;
}

/// <summary>An entry of a group's action log.</summary>
public class ChatAuditEntry
{
    public long Id { get; set; }
    public Guid ChatId { get; set; }
    public Guid? ActorId { get; set; }
    public string Action { get; set; } = string.Empty;
    public Guid? TargetUserId { get; set; }

    /// <summary>Details as JSON; null — none.</summary>
    public string? DataJson { get; set; }

    public DateTime CreatedAt { get; set; }
}
