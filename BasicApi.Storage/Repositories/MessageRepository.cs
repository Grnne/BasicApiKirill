using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Storage.Repositories;

public class MessageRepository(IDbSession db) : IMessageRepository
{
    /// <summary>
    /// Cursor-based pagination with sender name via JOIN — avoids N+1 lookups.
    /// </summary>
    public async Task<CursorResult<MessageWithSender>> GetMessagesWithSenderCursorAsync(
        Guid chatId, string? cursor, int limit, CancellationToken ct = default)
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

        var rows = await db.QueryAsync<MessageWithSender>(sql, parameters, ct);

        List<MessageWithSender> items;
        MessageWithSender? extra = null;

        if (rows.Count > limit)
        {
            items = [.. rows.Take(limit)];
            extra = rows[limit];
        }
        else
        {
            items = [.. rows];
        }

        return new CursorResult<MessageWithSender>
        {
            Items = items,
            Extra = extra
        };
    }

    public Task<MessageWithSender> CreateAsync(Message message, CancellationToken ct = default)
    {
        // Имя отправителя — тем же запросом: оно нужно в событии о новом сообщении.
        const string sql = @"
            WITH inserted AS (
                INSERT INTO messages (id, chat_id, sender_id, text, created_at, is_deleted)
                VALUES (@Id, @ChatId, @SenderId, @Text, @CreatedAt, @IsDeleted)
                RETURNING id, chat_id, sender_id, text, created_at, is_deleted
            )
            SELECT i.id AS Id, i.chat_id AS ChatId, i.sender_id AS SenderId, i.text AS Text,
                   i.created_at AS CreatedAt, i.is_deleted AS IsDeleted,
                   COALESCE(u.display_name, 'Unknown') AS SenderName
            FROM inserted i
            LEFT JOIN users u ON u.id = i.sender_id";

        return db.QuerySingleAsync<MessageWithSender>(sql, message, ct);
    }

    public async Task<ReadPointerUpdate> MarkReadAsync(
        Guid chatId, Guid userId, Guid messageId, CancellationToken ct = default)
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

        var (found, moved) = await db.QuerySingleAsync<(long Found, long Moved)>(
            sql, new { chatId, userId, messageId }, ct);

        if (found == 0) return ReadPointerUpdate.MessageNotFound;
        return moved > 0 ? ReadPointerUpdate.Moved : ReadPointerUpdate.NotMoved;
    }

    /// <summary>
    /// Finds the oldest message strictly after the given moment. Used as an exclusive
    /// cursor by "jump to date": the page before it ends with the last message at or
    /// before the date, so that message is included.
    /// </summary>
    public Task<Message?> GetFirstMessageAfterDateAsync(Guid chatId, DateTime date, CancellationToken ct = default)
    {
        // Колонки перечислены явно: SELECT * не мапит snake_case (created_at → CreatedAt),
        // из-за этого «переход к дате» строил курсор от 0001-01-01 и возвращал пустоту.
        const string sql = @"
            SELECT id AS Id, chat_id AS ChatId, sender_id AS SenderId, text AS Text,
                   created_at AS CreatedAt, is_deleted AS IsDeleted
            FROM messages
            WHERE chat_id = @chatId
              AND created_at > @date
              AND is_deleted = false
            ORDER BY created_at, id
            LIMIT 1";

        return db.QueryFirstOrDefaultAsync<Message>(sql, new { chatId, date }, ct);
    }

        /// <summary>
    /// Full-text search for messages within a chat using PostgreSQL tsvector.
    /// Supports cursor-based pagination with the same (created_at, id) composite cursor pattern.
    /// Returns messages with sender names via JOIN.
    /// Uses plainto_tsquery for safe user input handling.
    /// Also returns the total count of matching messages.
    /// </summary>
    public async Task<(CursorResult<MessageWithSender> Result, int TotalCount)> SearchMessagesCursorAsync(
        Guid chatId, string query, string? cursor, int limit, CancellationToken ct = default)
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

        var rows = await db.QueryAsync<MessageWithSender>(sql, parameters, ct);

        // Всего совпадений — на каждой странице: клиент показывает «N результатов»
        // независимо от того, какую страницу загрузил (раньше на 2-й и далее был 0).
        int totalCount;
        {
            const string countSql = @"
                SELECT COUNT(*)
                FROM messages m
                WHERE m.chat_id = @chatId
                  AND m.is_deleted = false
                  AND to_tsvector('english', m.text) @@ plainto_tsquery('english', @query)";

            totalCount = await db.ExecuteScalarAsync<int>(countSql, new { chatId, query }, ct);
        }

        List<MessageWithSender> items;
        MessageWithSender? extra = null;

        if (rows.Count > limit)
        {
            items = [.. rows.Take(limit)];
            extra = rows[limit];
        }
        else
        {
            items = [.. rows];
        }

        return (new CursorResult<MessageWithSender>
        {
            Items = items,
            Extra = extra
        }, totalCount);
    }
}