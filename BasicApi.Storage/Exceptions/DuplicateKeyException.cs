namespace BasicApi.Storage.Exceptions;

/// <summary>
/// A unique constraint rejected the write. Lets callers translate a storage-level
/// race into a domain answer (409) without referencing Npgsql types.
/// </summary>
public class DuplicateKeyException(string message, Exception? inner = null)
    : Exception(message, inner);
