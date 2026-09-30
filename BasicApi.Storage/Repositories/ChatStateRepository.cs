using BasicApi.Storage.Interfaces;

namespace BasicApi.Storage.Repositories;

public sealed class ChatStateRepository(IDbSession db) : IChatStateRepository
{
    public Task LockUserAsync(Guid userId, CancellationToken ct = default) =>
        db.ExecuteAsync("SELECT 1 FROM users WHERE id = @userId FOR UPDATE", new { userId }, ct);

    public Task<IReadOnlyList<Guid>> GetPinnedAsync(Guid userId, CancellationToken ct = default) =>
        db.QueryAsync<Guid>(@"
            SELECT chat_id FROM chat_members
            WHERE user_id = @userId AND pinned_position IS NOT NULL
            ORDER BY pinned_position, chat_id",
            new { userId }, ct);

    public Task SetPinnedAsync(Guid userId, IReadOnlyList<Guid> chatIds, CancellationToken ct = default) =>
        db.ExecuteAsync(@"
            UPDATE chat_members cm
            SET pinned_position = p.position
            FROM (
                SELECT m.chat_id, o.position
                FROM chat_members m
                LEFT JOIN unnest(@chatIds) WITH ORDINALITY AS o(chat_id, position) ON o.chat_id = m.chat_id
                WHERE m.user_id = @userId
            ) p
            WHERE cm.user_id = @userId AND cm.chat_id = p.chat_id
              AND cm.pinned_position IS DISTINCT FROM p.position",
            new { userId, chatIds = chatIds.ToArray() }, ct);

    public async Task<bool> SetArchivedAsync(Guid chatId, Guid userId, DateTime? archivedAt, CancellationToken ct = default) =>
        await db.ExecuteAsync(@"
            UPDATE chat_members SET archived_at = @archivedAt
            WHERE chat_id = @chatId AND user_id = @userId AND (archived_at IS NULL) <> (@archivedAt::timestamptz IS NULL)",
            new { chatId, userId, archivedAt }, ct) > 0;

    public async Task<bool> SetMutedUntilAsync(Guid chatId, Guid userId, DateTime? mutedUntil, CancellationToken ct = default) =>
        await db.ExecuteAsync(@"
            UPDATE chat_members SET muted_until = @mutedUntil
            WHERE chat_id = @chatId AND user_id = @userId AND muted_until IS DISTINCT FROM @mutedUntil",
            new { chatId, userId, mutedUntil }, ct) > 0;

    public Task<IReadOnlyList<Guid>> UnarchiveOnMessageAsync(
        Guid chatId, Guid senderId, DateTime now, CancellationToken ct = default) =>
        db.QueryAsync<Guid>(@"
            UPDATE chat_members SET archived_at = NULL
            WHERE chat_id = @chatId AND user_id <> @senderId AND archived_at IS NOT NULL
              AND (muted_until IS NULL OR muted_until <= @now)
            RETURNING user_id",
            new { chatId, senderId, now }, ct);

    public Task<ChatState?> GetAsync(Guid chatId, Guid userId, CancellationToken ct = default) =>
        db.QueryFirstOrDefaultAsync<ChatState>(@"
            SELECT pinned_position AS PinnedPosition, archived_at AS ArchivedAt, muted_until AS MutedUntil
            FROM chat_members WHERE chat_id = @chatId AND user_id = @userId",
            new { chatId, userId }, ct);
}
