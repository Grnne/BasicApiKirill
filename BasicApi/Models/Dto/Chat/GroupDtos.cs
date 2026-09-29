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

public class CreateGroupDto
{
    /// <summary>1–128 characters after trimming.</summary>
    public string? Title { get; set; }

    /// <summary>Who to add besides the creator; may be empty.</summary>
    public List<Guid>? MemberIds { get; set; }
}

public class AddMembersDto
{
    public List<Guid>? UserIds { get; set; }
}

public class UpdateGroupDto
{
    /// <summary>New title, 1–128 characters after trimming; null — unchanged.</summary>
    public string? Title { get; set; }

    /// <summary>Changes to the permissions members have by default; null — unchanged.</summary>
    public PermissionsPatchDto? MemberPermissions { get; set; }
}

public class SetRoleDto
{
    /// <summary><c>admin</c>, <c>member</c>, or <c>owner</c> — hand the group over.</summary>
    public string? Role { get; set; }
}

/// <summary>A member of a group as others see them.</summary>
public class GroupMemberDto
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = "member";

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
}

/// <summary><c>ChatDeleted</c>: the group is gone for everyone.</summary>
public class ChatDeletedDto
{
    public Guid ChatId { get; set; }
}

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
