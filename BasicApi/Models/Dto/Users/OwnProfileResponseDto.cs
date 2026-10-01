namespace BasicApi.Models.Dto.Users;

/// <summary>
/// The caller's own profile: <c>AuthResponseDto</c> minus the tokens. Unlike <c>UserProfileResponseDto</c>
/// it includes the email, which is private to the account owner.
/// </summary>
public class OwnProfileResponseDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>The photo; null — none. Fetched by <c>POST /api/media/links</c>.</summary>
    public Guid? AvatarId { get; set; }
}
