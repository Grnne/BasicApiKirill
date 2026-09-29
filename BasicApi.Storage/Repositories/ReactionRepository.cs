using BasicApi.Storage.Dto;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Storage.Repositories;

public sealed class ReactionRepository(IDbSession db) : IReactionRepository
{
    public Task<ReactionChange> SetAsync(
        Guid chatId, Guid messageId, Guid userId, string emoji, CancellationToken ct = default) =>
        ChangeAsync(chatId, messageId, userId, () => db.ExecuteAsync(@"
            INSERT INTO message_reactions (message_id, user_id, emoji) VALUES (@messageId, @userId, @emoji)
            ON CONFLICT (message_id, user_id) DO UPDATE SET emoji = EXCLUDED.emoji, created_at = now()
            WHERE message_reactions.emoji <> EXCLUDED.emoji",
            new { messageId, userId, emoji }, ct), ct);

    public Task<ReactionChange> RemoveAsync(Guid chatId, Guid messageId, Guid userId, CancellationToken ct = default) =>
        ChangeAsync(chatId, messageId, userId, () => db.ExecuteAsync(
            "DELETE FROM message_reactions WHERE message_id = @messageId AND user_id = @userId",
            new { messageId, userId }, ct), ct);

    /// <summary>
    /// Locks the message row first: reactions to one message are applied one at a time, and the
    /// summary — a separate statement with a fresh snapshot — sees every committed reaction.
    /// Computing it in the same statement as the change would miss a concurrent one.
    /// </summary>
    private Task<ReactionChange> ChangeAsync(
        Guid chatId, Guid messageId, Guid userId, Func<Task<int>> change, CancellationToken ct) =>
        db.InTransactionAsync(async ct =>
        {
            var found = await db.QueryFirstOrDefaultAsync<Guid?>(@"
                SELECT id FROM messages
                WHERE id = @messageId AND chat_id = @chatId AND deleted_at IS NULL
                  AND NOT EXISTS (SELECT 1 FROM hidden_messages h WHERE h.user_id = @userId AND h.message_id = @messageId)
                FOR UPDATE",
                new { messageId, chatId, userId }, ct);
            if (found is null)
                return ReactionChange.NotFound;

            if (await change() == 0)
                return new ReactionChange(true, false, await db.QueryFirstOrDefaultAsync<string?>(
                    "SELECT reactions_summary::text FROM messages WHERE id = @messageId", new { messageId }, ct));

            // Most popular first; among equals — the one that appeared first.
            var summary = await db.QueryFirstOrDefaultAsync<string?>(@"
                UPDATE messages SET reactions_summary = (
                    SELECT jsonb_agg(jsonb_build_object('emoji', emoji, 'count', n) ORDER BY n DESC, first_at)
                    FROM (
                        SELECT emoji, COUNT(*) AS n, MIN(created_at) AS first_at
                        FROM message_reactions WHERE message_id = @messageId
                        GROUP BY emoji
                    ) s
                )
                WHERE id = @messageId
                RETURNING reactions_summary::text",
                new { messageId }, ct);
            return new ReactionChange(true, true, summary);
        }, ct: ct);
}
