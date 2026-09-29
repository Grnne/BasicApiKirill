using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Exceptions;
using BasicApi.Storage.Interfaces;
using Npgsql;

namespace BasicApi.Storage.Repositories;

public class MessageRepository(IDbSession db) : IMessageRepository
{
    private const string SelectColumns = @"
        m.id AS Id,
        m.chat_id AS ChatId,
        m.sender_id AS SenderId,
        m.text AS Text,
        m.created_at AS CreatedAt,
        m.is_deleted AS IsDeleted,
        m.seq AS Seq,
        m.client_message_id AS ClientMessageId,
        COALESCE(u.display_name, 'Unknown') AS SenderName";

    /// <summary>
    /// Страница от новых к старым по seq; с запасом в одну строку — признак следующей страницы.
    /// </summary>
    public async Task<CursorResult<MessageWithSender>> GetMessagesWithSenderCursorAsync(
        Guid chatId, long? beforeSeq, int limit, CancellationToken ct = default)
    {
        var sql = $@"
            SELECT {SelectColumns}
            FROM messages m
            LEFT JOIN users u ON u.id = m.sender_id
            WHERE m.chat_id = @chatId
              AND m.is_deleted = false
              {(beforeSeq is null ? "" : "AND m.seq < @beforeSeq")}
            ORDER BY m.seq DESC
            LIMIT @fetchSize";

        var rows = await db.QueryAsync<MessageWithSender>(sql, new { chatId, beforeSeq, fetchSize = limit + 1 }, ct);
        return Page(rows, limit);
    }

    public Task<MessageWithSender> CreateAsync(
        Message message, Guid? clientMessageId = null, CancellationToken ct = default) =>
        db.InTransactionAsync(async ct =>
        {
            // Номер из счётчика чата. UPDATE блокирует строку чата до конца транзакции:
            // параллельные отправки в один чат получают номера по очереди, без дыр и повторов.
            // Имя отправителя — тем же запросом: оно нужно в событии о новом сообщении.
            const string sql = $@"
                WITH next AS (
                    UPDATE chats SET last_seq = last_seq + 1 WHERE id = @ChatId RETURNING last_seq
                ), m AS (
                    INSERT INTO messages (id, chat_id, sender_id, text, created_at, is_deleted, seq, client_message_id)
                    SELECT @Id, @ChatId, @SenderId, @Text, @CreatedAt, @IsDeleted, next.last_seq, @ClientMessageId
                    FROM next
                    RETURNING *
                )
                SELECT {SelectColumns}
                FROM m
                LEFT JOIN users u ON u.id = m.sender_id";

            try
            {
                return await db.QuerySingleAsync<MessageWithSender>(sql, new
                {
                    message.Id,
                    message.ChatId,
                    message.SenderId,
                    message.Text,
                    message.CreatedAt,
                    message.IsDeleted,
                    ClientMessageId = clientMessageId
                }, ct);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation &&
                                               ex.ConstraintName == "ux_messages_sender_id_client_message_id")
            {
                // Параллельный повтор той же отправки успел раньше. Транзакция откатится
                // вместе с выданным номером; вызывающий найдёт то сообщение.
                throw new DuplicateKeyException("Message with this clientMessageId already exists", ex);
            }
        }, ct: ct);

    public Task<MessageWithSender?> GetByClientMessageIdAsync(
        Guid senderId, Guid clientMessageId, CancellationToken ct = default) =>
        db.QueryFirstOrDefaultAsync<MessageWithSender>($@"
            SELECT {SelectColumns}
            FROM messages m
            LEFT JOIN users u ON u.id = m.sender_id
            WHERE m.sender_id = @senderId AND m.client_message_id = @clientMessageId",
            new { senderId, clientMessageId }, ct);

    public Task<long?> GetSeqAsync(Guid chatId, Guid messageId, CancellationToken ct = default) =>
        db.QueryFirstOrDefaultAsync<long?>(
            "SELECT seq FROM messages WHERE id = @messageId AND chat_id = @chatId", new { chatId, messageId }, ct);

    public async Task<ReadPointerUpdate> MarkReadAsync(
        Guid chatId, Guid userId, Guid messageId, CancellationToken ct = default)
    {
        // Одним запросом: сообщение должно быть из этого чата, а указатель движется
        // только вперёд — два устройства, отчитавшиеся не по порядку, не откатят
        // прочитанное назад.
        const string sql = @"
            WITH target AS (
                SELECT seq FROM messages WHERE id = @messageId AND chat_id = @chatId
            ), moved AS (
                UPDATE chat_members cm
                SET last_read_seq = t.seq
                FROM target t
                WHERE cm.chat_id = @chatId AND cm.user_id = @userId AND cm.last_read_seq < t.seq
                RETURNING 1
            )
            SELECT (SELECT COUNT(*) FROM target) AS Found, (SELECT COUNT(*) FROM moved) AS Moved";

        var (found, moved) = await db.QuerySingleAsync<(long Found, long Moved)>(
            sql, new { chatId, userId, messageId }, ct);

        if (found == 0) return ReadPointerUpdate.MessageNotFound;
        return moved > 0 ? ReadPointerUpdate.Moved : ReadPointerUpdate.NotMoved;
    }

    /// <summary>
    /// Номер самого раннего сообщения строго после момента. «Переход к дате» строит от
    /// него исключающий курсор: страница перед ним заканчивается последним сообщением
    /// на этот момент или раньше.
    /// </summary>
    public Task<long?> GetFirstSeqAfterAsync(Guid chatId, DateTime date, CancellationToken ct = default) =>
        db.QueryFirstOrDefaultAsync<long?>(@"
            SELECT MIN(seq) FROM messages
            WHERE chat_id = @chatId AND created_at > @date AND is_deleted = false",
            new { chatId, date }, ct);

    /// <summary>
    /// Full-text search within a chat, newest first by seq, with the total number of
    /// matches (same on every page). plainto_tsquery keeps user input safe.
    /// </summary>
    public async Task<(CursorResult<MessageWithSender> Result, int TotalCount)> SearchMessagesCursorAsync(
        Guid chatId, string query, long? beforeSeq, int limit, CancellationToken ct = default)
    {
        const string match = "to_tsvector('english', m.text) @@ plainto_tsquery('english', @query)";

        var sql = $@"
            SELECT {SelectColumns}
            FROM messages m
            LEFT JOIN users u ON u.id = m.sender_id
            WHERE m.chat_id = @chatId
              AND m.is_deleted = false
              AND {match}
              {(beforeSeq is null ? "" : "AND m.seq < @beforeSeq")}
            ORDER BY m.seq DESC
            LIMIT @fetchSize";

        var rows = await db.QueryAsync<MessageWithSender>(sql, new { chatId, query, beforeSeq, fetchSize = limit + 1 }, ct);

        // Всего совпадений — на каждой странице: клиент показывает «N результатов»
        // независимо от того, какую страницу загрузил.
        var totalCount = await db.ExecuteScalarAsync<int>($@"
            SELECT COUNT(*) FROM messages m
            WHERE m.chat_id = @chatId AND m.is_deleted = false AND {match}",
            new { chatId, query }, ct);

        return (Page(rows, limit), totalCount);
    }

    private static CursorResult<MessageWithSender> Page(IReadOnlyList<MessageWithSender> rows, int limit) =>
        rows.Count > limit
            ? new CursorResult<MessageWithSender> { Items = [.. rows.Take(limit)], Extra = rows[limit] }
            : new CursorResult<MessageWithSender> { Items = [.. rows] };
}
