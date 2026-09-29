using BasicApi.Storage.Dto;

namespace BasicApi.Storage.Interfaces;

public interface IReactionRepository
{
    /// <summary>
    /// Sets the user's reaction to the message (replacing their previous one) and recomputes the
    /// message's summary. A message of another chat, deleted for everyone or hidden by the user —
    /// not found.
    /// </summary>
    Task<ReactionChange> SetAsync(Guid chatId, Guid messageId, Guid userId, string emoji, CancellationToken ct = default);

    /// <summary>Removes the user's reaction and recomputes the summary.</summary>
    Task<ReactionChange> RemoveAsync(Guid chatId, Guid messageId, Guid userId, CancellationToken ct = default);
}
