namespace BasicApi.Middleware.Exceptions;

/// <summary>
/// Thrown when the user is authenticated but lacks permission. Maps to 403 Forbidden.
/// Use for "not a member", "no access" scenarios.
/// </summary>
public class ForbiddenException(string message, string errorCode = "FORBIDDEN")
    : DomainException(message, errorCode);
