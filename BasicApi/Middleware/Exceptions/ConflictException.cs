namespace BasicApi.Middleware.Exceptions;

/// <summary>
/// Thrown when the request conflicts with the current state. Maps to 409 Conflict.
/// </summary>
public class ConflictException(string message, string errorCode = "CONFLICT")
    : DomainException(message, errorCode);
