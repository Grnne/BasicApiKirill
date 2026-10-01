namespace BasicApi.Middleware.Exceptions;

/// <summary>A malformed or invalid request (400).</summary>
public class BadRequestException(string message, string errorCode = "BAD_REQUEST")
    : DomainException(message, errorCode);
