using System.Data;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Dapper;

namespace BasicApi.Storage.Repositories;

public class MessageRepository(IDbConnection connection) : IMessageRepository
{
    public async Task<IEnumerable<Message>> GetMessagesAsync(Guid chatId, DateTime? before, int limit)
    {
        var sql = @"
            SELECT * FROM messages 
            WHERE chat_id = @chatId 
            AND is_deleted = false";

        if (before.HasValue)
        {
            sql += " AND created_at < @before";
        }

        sql += " ORDER BY created_at DESC LIMIT @limit";

        return await connection.QueryAsync<Message>(sql, new { chatId, before, limit });
    }

    public async Task<Guid> CreateAsync(Message message)
    {
        const string sql = @"
            INSERT INTO messages (id, chat_id, sender_id, text, created_at, is_deleted) 
            VALUES (@Id, @ChatId, @SenderId, @Text, @CreatedAt, @IsDeleted)";

        await connection.ExecuteAsync(sql, message);
        return message.Id;
    }

    public async Task UpdateLastReadAsync(Guid chatId, Guid userId, Guid messageId)
    {
        const string sql = @"
            UPDATE chat_members 
            SET last_read_message_id = @messageId 
            WHERE chat_id = @chatId AND user_id = @userId";

        await connection.ExecuteAsync(sql, new { chatId, userId, messageId });
    }
    public async Task<int> GetUnreadCountAsync(Guid chatId, Guid userId)
    {
        const string sql = @"
        SELECT COUNT(*) 
        FROM messages m
        WHERE m.chat_id = @chatId 
        AND m.created_at > (
            SELECT COALESCE(
                (SELECT created_at FROM messages WHERE id = cm.last_read_message_id),
                '1970-01-01'::timestamp
            )
            FROM chat_members cm
            WHERE cm.chat_id = @chatId AND cm.user_id = @userId
        )";

        return await connection.ExecuteScalarAsync<int>(sql, new { chatId, userId });
    }
}