using System.Data;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Dapper;

namespace BasicApi.Storage.Repositories;

public class MessageRepository(IDbConnectionFactory connectionFactory) : IMessageRepository
{
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

        using var connection = connectionFactory.CreateConnection();
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

        using var connection = connectionFactory.CreateConnection();
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

                using var connection = connectionFactory.CreateConnection();
        await connection.ExecuteAsync(sql, message);
        return message.Id;
    }

    public async Task<ReadPointerUpdate> MarkReadAsync(Guid chatId, Guid userId, Guid messageId)
    {
        // Одним запросом: сообщение должно быть из этого чата, а указатель движется
        // только вперёд по (created_at, id) — два устройства, отчитавшиеся не по
        // порядку, не откатят прочитанное назад.
        const string sql = @"
            WITH target AS (
                SELECT id, created_at FROM messages WHERE id = @messageId AND chat_id = @chatId
            ), moved AS (
                UPDATE chat_members cm
                SET last_read_message_id = t.id
                FROM target t
                WHERE cm.chat_id = @chatId AND cm.user_id = @userId
                  AND (cm.last_read_message_id IS NULL
                       OR NOT EXISTS (SELECT 1 FROM messages r WHERE r.id = cm.last_read_message_id)
                       OR (t.created_at, t.id) > (
                           SELECT r.created_at, r.id FROM messages r WHERE r.id = cm.last_read_message_id))
                RETURNING 1
            )
            SELECT (SELECT COUNT(*) FROM target) AS Found, (SELECT COUNT(*) FROM moved) AS Moved";

        using var connection = connectionFactory.CreateConnection();
        var (found, moved) = await connection.QuerySingleAsync<(long Found, long Moved)>(
            sql, new { chatId, userId, messageId });

        if (found == 0) return ReadPointerUpdate.MessageNotFound;
        return moved > 0 ? ReadPointerUpdate.Moved : ReadPointerUpdate.NotMoved;
    }

        /// <summary>
    /// Finds the most recent message at or before the given date.
    /// Used to build a cursor for the "jump to date" endpoint.
    /// </summary>
    public async Task<Message?> GetFirstMessageBeforeDateAsync(Guid chatId, DateTime date)
    {
        const string sql = @"
            SELECT *
            FROM messages
            WHERE chat_id = @chatId
              AND created_at <= @date
              AND is_deleted = false
            ORDER BY created_at DESC, id DESC
            LIMIT 1";

        using var connection = connectionFactory.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Message>(sql, new { chatId, date });
    }

        /// <summary>
    /// Full-text search for messages within a chat using PostgreSQL tsvector.
    /// Supports cursor-based pagination with the same (created_at, id) composite cursor pattern.
    /// Returns messages with sender names via JOIN.
    /// Uses plainto_tsquery for safe user input handling.
    /// Also returns the total count of matching messages.
    /// </summary>
    public async Task<(CursorResult<MessageWithSender> Result, int TotalCount)> SearchMessagesCursorAsync(
        Guid chatId, string query, string? cursor, int limit)
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
                  AND to_tsvector('english', m.text) @@ plainto_tsquery('english', @query)
                  AND (m.created_at < @beforeTime
                       OR (m.created_at = @beforeTime AND m.id < @beforeId))
                ORDER BY m.created_at DESC, m.id DESC
                LIMIT @fetchSize";

            parameters = new { chatId, query, beforeTime, beforeId, fetchSize };
        }
        else
        {
            sql = $@"
                SELECT {selectColumns}
                FROM messages m
                LEFT JOIN users u ON u.id = m.sender_id
                WHERE m.chat_id = @chatId
                  AND m.is_deleted = false
                  AND to_tsvector('english', m.text) @@ plainto_tsquery('english', @query)
                ORDER BY m.created_at DESC, m.id DESC
                LIMIT @fetchSize";

            parameters = new { chatId, query, fetchSize };
        }

        using var connection = connectionFactory.CreateConnection();

        // Fetch the page
        var rows = (await connection.QueryAsync<MessageWithSender>(sql, parameters)).ToList();

        // Get total count (only on first page for performance)
        int totalCount = 0;
        if (string.IsNullOrEmpty(cursor))
        {
            const string countSql = @"
                SELECT COUNT(*)
                FROM messages m
                WHERE m.chat_id = @chatId
                  AND m.is_deleted = false
                  AND to_tsvector('english', m.text) @@ plainto_tsquery('english', @query)";

            totalCount = await connection.ExecuteScalarAsync<int>(countSql, new { chatId, query });
        }

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

        return (new CursorResult<MessageWithSender>
        {
            Items = items,
            Extra = extra
        }, totalCount);
    }
}