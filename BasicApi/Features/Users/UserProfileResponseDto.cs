using BasicApi.Models.Dto.Users;

namespace BasicApi.Features.Users;

/// <summary>
/// Public profile of a user, safe to show to any authenticated user. Email is deliberately absent —
/// see <see cref="OwnProfileResponseDto"/> for your own.
/// </summary>
public class UserProfileResponseDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>The photo; null — none. Fetched by <c>POST /api/media/links</c>.</summary>
    public Guid? AvatarId { get; set; }
}
