namespace BasicApi.Middleware.Exceptions;

/// <summary>
/// Base class for all domain-level exceptions.
/// Enables the middleware to distinguish domain errors from system errors.
/// </summary>
public abstract class DomainException(string message, string errorCode) : Exception(message)
{
    /// <summary>
    /// Machine-readable error code for programmatic handling by clients.
    /// </summary>
    public string ErrorCode { get; } = errorCode;
}
