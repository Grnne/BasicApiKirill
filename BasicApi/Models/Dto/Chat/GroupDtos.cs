using System.Text.Json.Serialization;

namespace BasicApi.Models.Dto.Chat;

/// <summary>What a member of a group may do (D8). In answers — all fields are set.</summary>
public class GroupPermissionsDto
{
    public bool SendMessages { get; set; }
    public bool SendMedia { get; set; }
    public bool AddMembers { get; set; }
    public bool ChangeInfo { get; set; }

    /// <summary>Admin permission: remove members (not admins).</summary>
    public bool RemoveMembers { get; set; }

    /// <summary>Admin permission: delete others' messages for everyone, at any time.</summary>
    public bool DeleteMessages { get; set; }

    /// <summary>Admin permission: make members admins.</summary>
    public bool AddAdmins { get; set; }
}

/// <summary>A change of permissions: only the given fields change, the others stay as they are.</summary>
public class PermissionsPatchDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? SendMessages { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? SendMedia { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? AddMembers { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? ChangeInfo { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? RemoveMembers { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? DeleteMessages { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? AddAdmins { get; set; }
}

/// <summary>A member of a group as others see them.</summary>
public class GroupMemberDto
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = "member";

    /// <summary>The photo; null — none. Fetched by <c>POST /api/media/links</c>.</summary>
    public Guid? AvatarId { get; set; }

    /// <summary>What the member may do now: the role, the group's defaults and their own overrides.</summary>
    public GroupPermissionsDto Permissions { get; set; } = new();
}

/// <summary><c>MemberAdded</c>: new members of a group.</summary>
public class MembersAddedDto
{
    public Guid ChatId { get; set; }
    public Guid AddedBy { get; set; }
    public List<GroupMemberDto> Members { get; set; } = [];
}

/// <summary><c>MemberRemoved</c>: someone left the group or was removed.</summary>
public class MemberRemovedDto
{
    public Guid ChatId { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Who removed them; null — they left.</summary>
    public Guid? RemovedBy { get; set; }
}

/// <summary><c>MemberUpdated</c>: a member's role or permissions changed.</summary>
public class MemberUpdatedDto
{
    public Guid ChatId { get; set; }
    public GroupMemberDto Member { get; set; } = new();
}

/// <summary><c>ChatUpdated</c>: the group's title or default permissions changed.</summary>
public class ChatUpdatedDto
{
    public Guid ChatId { get; set; }
    public string? Title { get; set; }
    public GroupPermissionsDto MemberPermissions { get; set; } = new();

    /// <summary>The group's photo; null — none.</summary>
    public Guid? AvatarId { get; set; }
}

/// <summary><c>ChatDeleted</c>: the group is gone for everyone.</summary>
public class ChatDeletedDto
{
    public Guid ChatId { get; set; }
}
