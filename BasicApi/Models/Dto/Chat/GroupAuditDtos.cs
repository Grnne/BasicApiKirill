namespace BasicApi.Models.Dto.Chat;

/// <summary>An entry of the group's action log.</summary>
public class AuditEntryDto
{
    public long Id { get; set; }

    /// <summary>
    /// <c>group_created</c>, <c>title_changed</c>, <c>member_permissions_changed</c>,
    /// <c>members_added</c>, <c>member_removed</c>, <c>member_left</c>, <c>role_changed</c>,
    /// <c>permissions_changed</c>, <c>ownership_transferred</c>, <c>message_deleted</c>.
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Who did it; null — the account is gone.</summary>
    public Guid? ActorId { get; set; }

    /// <summary>Whom it concerned, if anyone.</summary>
    public Guid? TargetUserId { get; set; }

    /// <summary>Details: the title, the role, the permissions, the message.</summary>
    public System.Text.Json.JsonElement? Data { get; set; }

    public DateTime CreatedAt { get; set; }
}

public class AuditPageDto
{
    public List<AuditEntryDto> Items { get; set; } = [];

    /// <summary>Pass as <c>cursor</c> for older entries; null — no more.</summary>
    public string? NextCursor { get; set; }

    public bool HasMore { get; set; }
}
