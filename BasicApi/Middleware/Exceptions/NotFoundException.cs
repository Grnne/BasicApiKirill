namespace BasicApi.Middleware.Exceptions;

/// <summary>The requested resource was not found (404).</summary>
public class NotFoundException(string message, string errorCode = "NOT_FOUND")
    : DomainException(message, errorCode);
