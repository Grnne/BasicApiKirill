namespace BasicApi.Middleware.Exceptions;

/// <summary>
/// Thrown when the request is malformed or invalid. Maps to 400 Bad Request.
/// </summary>
public class BadRequestException(string message, string errorCode = "BAD_REQUEST")
    : DomainException(message, errorCode);
