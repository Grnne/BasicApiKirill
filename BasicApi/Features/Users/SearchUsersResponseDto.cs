namespace BasicApi.Features.Users;

/// <summary>
/// A single user result in a search response.
/// </summary>
public class UserSearchResultDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }

    /// <summary>The photo; null — none. Fetched by <c>POST /api/media/links</c>.</summary>
    public Guid? AvatarId { get; set; }
}

/// <summary>
/// Response for user search by name/username.
/// </summary>
public class SearchUsersResponseDto
{
    /// <summary>Matching user items.</summary>
    public List<UserSearchResultDto> Items { get; set; } = [];

    /// <summary>The original search query.</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>Total number of matching users.</summary>
    public int TotalCount { get; set; }
}
