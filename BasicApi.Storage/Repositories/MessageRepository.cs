using System.Text.RegularExpressions;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Exceptions;
using BasicApi.Storage.Interfaces;
using Npgsql;

namespace BasicApi.Storage.Repositories;

public partial class MessageRepository(IDbSession db) : IMessageRepository
{
    private const string SelectColumns = @"
        m.id AS Id,
        m.chat_id AS ChatId,
        m.sender_id AS SenderId,
        m.text AS Text,
        m.created_at AS CreatedAt,
        m.type AS Type,
        m.edited_at AS EditedAt,
        m.deleted_at AS DeletedAt,
        m.entities::text AS EntitiesJson,
        m.seq AS Seq,
        m.client_message_id AS ClientMessageId,
        COALESCE(u.display_name, 'Unknown') AS SenderName,
        m.reply_to_message_id AS ReplyToMessageId,
        r.sender_id AS ReplyToSenderId,
        ru.display_name AS ReplyToSenderName,
        r.text AS ReplyToText,
        COALESCE(r.deleted_at IS NOT NULL, false) AS ReplyToDeleted,
        m.forward_from_user_id AS ForwardFromUserId,
        fu.display_name AS ForwardFromUserName,
        m.forward_from_chat_id AS ForwardFromChatId,
        m.forward_from_message_id AS ForwardFromMessageId";

    /// <summary>Sender, the answered message with its author, the original author of a forward.</summary>
    private const string Joins = @"
        LEFT JOIN users u ON u.id = m.sender_id
        LEFT JOIN messages r ON r.id = m.reply_to_message_id
        LEFT JOIN users ru ON ru.id = r.sender_id
        LEFT JOIN users fu ON fu.id = m.forward_from_user_id";

    /// <summary>
    /// What a member sees: not deleted for everyone and not hidden by them ("delete for me").
    /// </summary>
    private const string VisibleToViewer = @"
        m.deleted_at IS NULL
        AND NOT EXISTS (SELECT 1 FROM hidden_messages h WHERE h.user_id = @viewerId AND h.message_id = m.id)";

    /// <summary>
    /// Page from newest to oldest by seq; with one extra row — the sign of a next page.
    /// </summary>
    public async Task<CursorResult<MessageWithSender>> GetMessagesWithSenderCursorAsync(
        Guid chatId, Guid viewerId, long? beforeSeq, int limit, CancellationToken ct = default)
    {
        var sql = $@"
            SELECT {SelectColumns}
            FROM messages m
            {Joins}
            WHERE m.chat_id = @chatId
              AND {VisibleToViewer}
              {(beforeSeq is null ? "" : "AND m.seq < @beforeSeq")}
            ORDER BY m.seq DESC
            LIMIT @fetchSize";

        var rows = await db.QueryAsync<MessageWithSender>(
            sql, new { chatId, viewerId, beforeSeq, fetchSize = limit + 1 }, ct);
        return Page(rows, limit);
    }

    public Task<MessageWithSender> CreateAsync(
        Message message, Guid? clientMessageId = null, CancellationToken ct = default) =>
        db.InTransactionAsync(async ct =>
        {
            // The number comes from the chat counter. UPDATE locks the chat row until the transaction ends:
            // concurrent sends to one chat get numbers in turn, with no gaps or repeats.
            // The sender name comes from the same query: it is needed in the new-message event.
            const string sql = $@"
                WITH next AS (
                    UPDATE chats SET last_seq = last_seq + 1 WHERE id = @ChatId RETURNING last_seq
                ), m AS (
                    INSERT INTO messages (id, chat_id, sender_id, text, created_at, type, seq, client_message_id,
                                          reply_to_message_id, forward_from_user_id, forward_from_chat_id,
                                          forward_from_message_id, entities)
                    SELECT @Id, @ChatId, @SenderId, @Text, @CreatedAt, @Type, next.last_seq, @ClientMessageId,
                           @ReplyToMessageId, @ForwardFromUserId, @ForwardFromChatId, @ForwardFromMessageId,
                           @EntitiesJson::jsonb
                    FROM next
                    RETURNING *
                )
                SELECT {SelectColumns}
                FROM m
                {Joins}";

            try
            {
                return await db.QuerySingleAsync<MessageWithSender>(sql, new
                {
                    message.Id,
                    message.ChatId,
                    message.SenderId,
                    message.Text,
                    message.CreatedAt,
                    message.Type,
                    ClientMessageId = clientMessageId,
                    message.ReplyToMessageId,
                    message.ForwardFromUserId,
                    message.ForwardFromChatId,
                    message.ForwardFromMessageId,
                    message.EntitiesJson
                }, ct);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation &&
                                               ex.ConstraintName == "ux_messages_sender_id_client_message_id")
            {
                // A concurrent retry of the same send got there first. The transaction will roll back
                // together with the issued number; the caller will find that message.
                throw new DuplicateKeyException("Message with this clientMessageId already exists", ex);
            }
        }, ct: ct);

    public Task<MessageWithSender?> GetByClientMessageIdAsync(
        Guid senderId, Guid clientMessageId, CancellationToken ct = default) =>
        db.QueryFirstOrDefaultAsync<MessageWithSender>($@"
            SELECT {SelectColumns}
            FROM messages m
            {Joins}
            WHERE m.sender_id = @senderId AND m.client_message_id = @clientMessageId",
            new { senderId, clientMessageId }, ct);

    public Task<MessageWithSender?> GetAsync(Guid chatId, Guid messageId, CancellationToken ct = default) =>
        db.QueryFirstOrDefaultAsync<MessageWithSender>($@"
            SELECT {SelectColumns}
            FROM messages m
            {Joins}
            WHERE m.id = @messageId AND m.chat_id = @chatId",
            new { chatId, messageId }, ct);

    public Task<IReadOnlyList<MessageWithSender>> GetVisibleAsync(
        Guid chatId, Guid viewerId, IReadOnlyCollection<Guid> messageIds, CancellationToken ct = default) =>
        db.QueryAsync<MessageWithSender>($@"
            SELECT {SelectColumns}
            FROM messages m
            {Joins}
            WHERE m.chat_id = @chatId AND m.id = ANY(@messageIds) AND {VisibleToViewer}
            ORDER BY m.seq",
            new { chatId, viewerId, messageIds = messageIds.ToArray() }, ct);

    public Task<MessageWithSender?> EditTextAsync(
        Guid messageId, string text, string? entitiesJson, DateTime editedAt, CancellationToken ct = default) =>
        // A message deleted in the meantime is not revived: the update finds nothing.
        db.QueryFirstOrDefaultAsync<MessageWithSender>($@"
            WITH m AS (
                UPDATE messages SET text = @text, entities = @entitiesJson::jsonb, edited_at = @editedAt
                WHERE id = @messageId AND deleted_at IS NULL
                RETURNING *
            )
            SELECT {SelectColumns}
            FROM m
            {Joins}",
            new { messageId, text, entitiesJson, editedAt }, ct);

    public async Task<bool> DeleteForEveryoneAsync(Guid messageId, DateTime deletedAt, CancellationToken ct = default) =>
        await db.ExecuteAsync(@"
            UPDATE messages SET deleted_at = @deletedAt, text = '', entities = NULL
            WHERE id = @messageId AND deleted_at IS NULL",
            new { messageId, deletedAt }, ct) > 0;

    // Two statements, not a CTE: a DELETE and an INSERT of the same key in one statement
    // see the same snapshot, and the INSERT would still hit the old row.
    public Task SetMentionsAsync(
        Guid messageId, Guid chatId, long seq, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default) =>
        db.InTransactionAsync(async ct =>
        {
            await db.ExecuteAsync("DELETE FROM message_mentions WHERE message_id = @messageId", new { messageId }, ct);
            if (userIds.Count > 0)
                await db.ExecuteAsync(@"
                    INSERT INTO message_mentions (message_id, user_id, chat_id, seq)
                    SELECT @messageId, u, @chatId, @seq FROM unnest(@userIds) AS u
                    ON CONFLICT DO NOTHING",
                    new { messageId, chatId, seq, userIds = userIds.Distinct().ToArray() }, ct);
            return true;
        }, ct: ct);

    public async Task<bool> HideAsync(Guid userId, Guid messageId, CancellationToken ct = default) =>
        await db.ExecuteAsync(@"
            INSERT INTO hidden_messages (user_id, message_id) VALUES (@userId, @messageId)
            ON CONFLICT DO NOTHING",
            new { userId, messageId }, ct) > 0;

    public Task<long?> GetSeqAsync(Guid chatId, Guid messageId, CancellationToken ct = default) =>
        db.QueryFirstOrDefaultAsync<long?>(
            "SELECT seq FROM messages WHERE id = @messageId AND chat_id = @chatId", new { chatId, messageId }, ct);

    public async Task<ReadPointerUpdate> MarkReadAsync(
        Guid chatId, Guid userId, Guid messageId, CancellationToken ct = default)
    {
        // In one query: the message must belong to this chat, and the pointer moves
        // only forward — two devices reporting out of order will not roll back
        // what has been read.
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
    /// Number of the earliest message strictly after the moment. "Jump to date" builds an exclusive
    /// cursor from it: the page before it ends with the last message
    /// at that moment or earlier.
    /// </summary>
    public Task<long?> GetFirstSeqAfterAsync(Guid chatId, DateTime date, CancellationToken ct = default) =>
        db.QueryFirstOrDefaultAsync<long?>(@"
            SELECT MIN(seq) FROM messages
            WHERE chat_id = @chatId AND created_at > @date AND deleted_at IS NULL",
            new { chatId, date }, ct);

    /// <summary>
    /// Full-text search within a chat, newest first by seq, with the total number of
    /// matches (same on every page). The query config must match the one of
    /// messages.search_vector ('russian').
    /// </summary>
    public async Task<(CursorResult<MessageWithSender> Result, int TotalCount)> SearchMessagesCursorAsync(
        Guid chatId, Guid viewerId, string query, long? beforeSeq, int limit, CancellationToken ct = default)
    {
        const string match = "m.search_vector @@ to_tsquery('russian', @prefixQuery)";
        var prefixQuery = ToPrefixQuery(query);

        var sql = $@"
            SELECT {SelectColumns}
            FROM messages m
            {Joins}
            WHERE m.chat_id = @chatId
              AND {VisibleToViewer}
              AND {match}
              {(beforeSeq is null ? "" : "AND m.seq < @beforeSeq")}
            ORDER BY m.seq DESC
            LIMIT @fetchSize";

        var rows = await db.QueryAsync<MessageWithSender>(
            sql, new { chatId, viewerId, prefixQuery, beforeSeq, fetchSize = limit + 1 }, ct);

        // Total matches — on every page: the client shows "N results"
        // regardless of which page it loaded.
        var totalCount = await db.ExecuteScalarAsync<int>($@"
            SELECT COUNT(*) FROM messages m
            WHERE m.chat_id = @chatId AND {VisibleToViewer} AND {match}",
            new { chatId, viewerId, prefixQuery }, ct);

        return (Page(rows, limit), totalCount);
    }

    /// <summary>
    /// Query words are matched as word prefixes: "запуск" finds both "запускаем" and "до запуска"
    /// (the Russian stemmer reduces them to different stems), and the query works as you type.
    /// Only letters and digits are taken from the input, so tsquery operators cannot get through.
    /// Stop words ("и", "в", "the") are dropped by the dictionary itself.
    /// </summary>
    internal static string ToPrefixQuery(string query) =>
        string.Join(" & ", WordPattern().Matches(query).Select(m => m.Value + ":*"));

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordPattern();

    private static CursorResult<MessageWithSender> Page(IReadOnlyList<MessageWithSender> rows, int limit) =>
        rows.Count > limit
            ? new CursorResult<MessageWithSender> { Items = [.. rows.Take(limit)], Extra = rows[limit] }
            : new CursorResult<MessageWithSender> { Items = [.. rows] };
}
