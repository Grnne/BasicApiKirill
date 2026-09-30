namespace BasicApi.Models.Dto.Users;

/// <summary>
/// The caller's own profile — <c>AuthResponseDto</c> minus the token fields,
/// so a client restoring a session from a stored token recovers exactly the user data
/// login/register would have given it.
/// Unlike <c>UserProfileResponseDto</c> this includes the email, which is
/// private to the account owner.
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
