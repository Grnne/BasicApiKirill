namespace BasicApi.Middleware.Exceptions;

/// <summary>
/// Thrown when authentication fails. Maps to 401 Unauthorized.
/// Use for invalid credentials, missing/invalid tokens.
/// </summary>
public class UnauthorizedException(string message, string errorCode = "UNAUTHORIZED")
    : DomainException(message, errorCode);
