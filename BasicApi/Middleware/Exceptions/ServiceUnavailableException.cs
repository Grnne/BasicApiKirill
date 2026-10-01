namespace BasicApi.Middleware.Exceptions;

/// <summary>A part of the service is switched off or out of reach, e.g. no file storage (503).</summary>
public class ServiceUnavailableException(string message, string errorCode = "SERVICE_UNAVAILABLE")
    : DomainException(message, errorCode);
