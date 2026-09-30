namespace BasicApi.Models.Dto.Users;

/// <summary><c>BlockListChanged</c>: the user blocked or unblocked someone — to the user's devices.</summary>
public class BlockListChangedDto
{
    public Guid UserId { get; set; }
    public bool Blocked { get; set; }
}
