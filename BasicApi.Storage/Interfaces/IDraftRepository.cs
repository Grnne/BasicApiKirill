using BasicApi.Storage.Entities;

namespace BasicApi.Storage.Interfaces;

public interface IDraftRepository
{
    Task<Draft?> GetAsync(Guid userId, Guid chatId, CancellationToken ct = default);

    /// <summary>Creates or replaces the draft.</summary>
    Task SaveAsync(Draft draft, CancellationToken ct = default);

    /// <summary>Removes the draft; false when there was none.</summary>
    Task<bool> DeleteAsync(Guid userId, Guid chatId, CancellationToken ct = default);
}
