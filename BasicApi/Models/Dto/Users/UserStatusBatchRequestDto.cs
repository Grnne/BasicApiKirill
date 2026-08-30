namespace BasicApi.Models.Dto.Users;

/// <summary>
/// Request body for POST /api/users/status — online status for an explicit set of users.
/// </summary>
public class UserStatusBatchRequestDto
{
    /// <summary>Upper bound on the number of IDs accepted in a single request.</summary>
    public const int MaxUserIds = 200;

    /// <summary>
    /// User IDs to resolve. Must be non-empty and contain at most
    /// <see cref="MaxUserIds"/> entries. Duplicates are collapsed.
    /// </summary>
    public List<Guid> UserIds { get; set; } = [];
}
