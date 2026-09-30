using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Storage.Repositories;

public sealed class DraftRepository(IDbSession db) : IDraftRepository
{
    public Task<Draft?> GetAsync(Guid userId, Guid chatId, CancellationToken ct = default) =>
        db.QueryFirstOrDefaultAsync<Draft>(@"
            SELECT user_id AS UserId, chat_id AS ChatId, text AS Text, entities::text AS EntitiesJson,
                   reply_to_message_id AS ReplyToMessageId, updated_at AS UpdatedAt
            FROM user_drafts
            WHERE user_id = @userId AND chat_id = @chatId",
            new { userId, chatId }, ct);

    public Task SaveAsync(Draft draft, CancellationToken ct = default) =>
        db.ExecuteAsync(@"
            INSERT INTO user_drafts (user_id, chat_id, text, entities, reply_to_message_id, updated_at)
            VALUES (@UserId, @ChatId, @Text, @EntitiesJson::jsonb, @ReplyToMessageId, @UpdatedAt)
            ON CONFLICT (user_id, chat_id) DO UPDATE
            SET text = EXCLUDED.text, entities = EXCLUDED.entities,
                reply_to_message_id = EXCLUDED.reply_to_message_id, updated_at = EXCLUDED.updated_at",
            draft, ct);

    public async Task<bool> DeleteAsync(Guid userId, Guid chatId, CancellationToken ct = default) =>
        await db.ExecuteAsync(
            "DELETE FROM user_drafts WHERE user_id = @userId AND chat_id = @chatId", new { userId, chatId }, ct) > 0;
}
