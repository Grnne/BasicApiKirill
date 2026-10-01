namespace BasicApi.Middleware.Exceptions;

/// <summary>The request conflicts with the current state (409).</summary>
public class ConflictException(string message, string errorCode = "CONFLICT")
    : DomainException(message, errorCode);
