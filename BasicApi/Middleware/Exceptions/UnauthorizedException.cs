namespace BasicApi.Middleware.Exceptions;

/// <summary>Authentication failed: invalid credentials or a missing/invalid token (401).</summary>
public class UnauthorizedException(string message, string errorCode = "UNAUTHORIZED")
    : DomainException(message, errorCode);
