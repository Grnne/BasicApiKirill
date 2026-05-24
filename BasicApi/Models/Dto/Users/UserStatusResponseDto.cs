namespace BasicApi.Models.Dto.Users;

public class UserStatusDto
{
    public Guid UserId { get; set; }
    public bool IsOnline { get; set; }
}

public class UserStatusResponseDto
{
    public List<UserStatusDto> Items { get; set; } = [];
}
