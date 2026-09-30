namespace BasicApi.Models.Dto.Chat;

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
