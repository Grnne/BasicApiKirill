namespace BasicApi.Middleware.Exceptions;

/// <summary>
/// Thrown when a requested resource was not found. Maps to 404 Not Found.
/// </summary>
public class NotFoundException(string message, string errorCode = "NOT_FOUND")
    : DomainException(message, errorCode);
