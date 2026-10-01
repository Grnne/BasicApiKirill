namespace BasicApi.Middleware.Exceptions;

/// <summary>Base of expected domain errors, which the middleware answers with their code instead of a 500.</summary>
public abstract class DomainException(string message, string errorCode) : Exception(message)
{
    /// <summary>Machine-readable error code for clients.</summary>
    public string ErrorCode { get; } = errorCode;
}
