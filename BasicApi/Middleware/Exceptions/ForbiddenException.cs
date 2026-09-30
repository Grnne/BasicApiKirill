namespace BasicApi.Middleware.Exceptions;

/// <summary>The user is authenticated but may not do this, e.g. not a member (403).</summary>
public class ForbiddenException(string message, string errorCode = "FORBIDDEN")
    : DomainException(message, errorCode);
