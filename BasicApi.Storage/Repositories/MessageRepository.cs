using System.Data;
using BasicApi.Storage.Dto;
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

    public async Task<CursorResult<Message>> GetMessagesCursorAsync(Guid chatId, string? cursor, int limit)
    {
        // Decode cursor — if null, start from the most recent messages
        DateTime? beforeTime = null;
        Guid? beforeId = null;

        if (!string.IsNullOrEmpty(cursor))
        {
            var decoded = CursorDto.Decode(cursor);
            beforeTime = decoded.CreatedAt;
            beforeId = decoded.Id;
        }

        // Fetch limit+1 to detect if there are more pages
        var fetchSize = limit + 1;
        string sql;
        object parameters;

        // Use composite pagination: (created_at, id) < (@beforeTime, @beforeId)
        // The row-level comparison ensures deterministic ordering even when
        // multiple messages share the same timestamp.
        if (beforeTime is not null && beforeId is not null)
        {
            sql = @"
                SELECT * FROM messages
                WHERE chat_id = @chatId
                  AND is_deleted = false
                  AND (created_at < @beforeTime
                       OR (created_at = @beforeTime AND id < @beforeId))
                ORDER BY created_at DESC, id DESC
                LIMIT @fetchSize";

            parameters = new { chatId, beforeTime, beforeId, fetchSize };
        }
        else
        {
            sql = @"
                SELECT * FROM messages
                WHERE chat_id = @chatId
                  AND is_deleted = false
                ORDER BY created_at DESC, id DESC
                LIMIT @fetchSize";

            parameters = new { chatId, fetchSize };
        }

        var rows = (await connection.QueryAsync<Message>(sql, parameters)).ToList();

        // Determine page items and the "extra" record
        List<Message> items;
        Message? extra = null;

        if (rows.Count > limit)
        {
            items = rows.Take(limit).ToList();
            extra = rows[limit];
        }
        else
        {
            items = rows;
        }

        return new CursorResult<Message>
        {
            Items = items,
            Extra = extra
        };
    }

    /// <summary>
    /// Cursor-based pagination with sender name via JOIN — avoids N+1 lookups.
    /// </summary>
    public async Task<CursorResult<MessageWithSender>> GetMessagesWithSenderCursorAsync(
        Guid chatId, string? cursor, int limit)
    {
        DateTime? beforeTime = null;
        Guid? beforeId = null;

        if (!string.IsNullOrEmpty(cursor))
        {
            var decoded = CursorDto.Decode(cursor);
            beforeTime = decoded.CreatedAt;
            beforeId = decoded.Id;
        }

        var fetchSize = limit + 1;
        string sql;
        object parameters;

        const string selectColumns = @"
                m.id AS Id,
                m.chat_id AS ChatId,
                m.sender_id AS SenderId,
                m.text AS Text,
                m.created_at AS CreatedAt,
                m.is_deleted AS IsDeleted,
                COALESCE(u.display_name, 'Unknown') AS SenderName";

        if (beforeTime is not null && beforeId is not null)
        {
            sql = $@"
                SELECT {selectColumns}
                FROM messages m
                LEFT JOIN users u ON u.id = m.sender_id
                WHERE m.chat_id = @chatId
                  AND m.is_deleted = false
                  AND (m.created_at < @beforeTime
                       OR (m.created_at = @beforeTime AND m.id < @beforeId))
                ORDER BY m.created_at DESC, m.id DESC
                LIMIT @fetchSize";

            parameters = new { chatId, beforeTime, beforeId, fetchSize };
        }
        else
        {
            sql = $@"
                SELECT {selectColumns}
                FROM messages m
                LEFT JOIN users u ON u.id = m.sender_id
                WHERE m.chat_id = @chatId
                  AND m.is_deleted = false
                ORDER BY m.created_at DESC, m.id DESC
                LIMIT @fetchSize";

            parameters = new { chatId, fetchSize };
        }

        var rows = (await connection.QueryAsync<MessageWithSender>(sql, parameters)).ToList();

        List<MessageWithSender> items;
        MessageWithSender? extra = null;

        if (rows.Count > limit)
        {
            items = rows.Take(limit).ToList();
            extra = rows[limit];
        }
        else
        {
            items = rows;
        }

        return new CursorResult<MessageWithSender>
        {
            Items = items,
            Extra = extra
        };
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