namespace BasicApi.Models.Dto.Users;

/// <summary>
/// Public profile of a user, safe to show to any authenticated user.
/// Carries the same fields as <see cref="UserSearchResultDto"/>, so a client can
/// store search results and profile lookups as one record.
/// Email is deliberately absent — see <see cref="OwnProfileResponseDto"/> for your own.
/// </summary>
public class UserProfileResponseDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}
