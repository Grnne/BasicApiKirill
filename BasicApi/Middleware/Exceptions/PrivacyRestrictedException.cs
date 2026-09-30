namespace BasicApi.Middleware.Exceptions;

/// <summary>
/// A user's privacy settings or block forbid the action (403 <c>PRIVACY_RESTRICTED</c>);
/// <see cref="UserIds"/> — whose, when the action concerns several users.
/// </summary>
public sealed class PrivacyRestrictedException(string message, IReadOnlyList<Guid>? userIds = null)
    : ForbiddenException(message, Code)
{
    public const string Code = "PRIVACY_RESTRICTED";

    public IReadOnlyList<Guid>? UserIds { get; } = userIds;
}
