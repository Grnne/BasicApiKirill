using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Storage.Repositories;

public class ChatRepository(IDbSession db) : IChatRepository
{
    public Task<IReadOnlyList<Guid>> GetUserChatIdsAsync(Guid userId, CancellationToken ct = default) =>
        db.QueryAsync<Guid>("SELECT chat_id FROM chat_members WHERE user_id = @userId", new { userId }, ct);

    public Task<List<ChatListResult>> GetUserChatsBatchedAsync(Guid userId, CancellationToken ct = default)
        => SearchChatsBatchedAsync(userId, null, null, null, ct);

    public Task<Chat?> GetByIdAsync(Guid chatId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT
                id AS Id,
                title AS Title,
                type AS Type,
                created_at AS CreatedAt
            FROM chats
            WHERE id = @chatId";

        return db.QueryFirstOrDefaultAsync<Chat>(sql, new { chatId }, ct);
    }

    /// <summary>Ключ пары для личного чата; порядок участников не важен.</summary>
    private const string PrivateKeySql =
        "LEAST(@userId, @otherUserId)::text || ':' || GREATEST(@userId, @otherUserId)::text";

    public Task<(Guid ChatId, bool Created)> GetOrCreatePrivateChatAsync(
        Guid userId, Guid otherUserId, CancellationToken ct = default) =>
        db.InTransactionAsync(async ct =>
        {
            // Уникальный индекс по private_key решает гонку: параллельная вставка той же
            // пары ждёт коммита первой и получает DO NOTHING, а не второй чат.
            var chatId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var inserted = await db.QueryFirstOrDefaultAsync<Guid?>($@"
                INSERT INTO chats (id, title, type, created_at, private_key)
                VALUES (@chatId, NULL, 'private', @now, {PrivateKeySql})
                ON CONFLICT (private_key) DO NOTHING
                RETURNING id",
                new { chatId, userId, otherUserId, now }, ct);

            if (inserted is not null)
            {
                await db.ExecuteAsync(@"
                    INSERT INTO chat_members (chat_id, user_id, joined_at)
                    VALUES (@chatId, @userId, @now), (@chatId, @otherUserId, @now)",
                    new { chatId, userId, otherUserId, now }, ct);
                return (chatId, true);
            }

            var existing = await db.QuerySingleAsync<Guid>(
                $"SELECT id FROM chats WHERE private_key = {PrivateKeySql}",
                new { userId, otherUserId }, ct);
            return (existing, false);
        }, ct: ct);

    public async Task<bool> IsMemberAsync(Guid chatId, Guid userId, CancellationToken ct = default)
    {
        const string sql = "SELECT EXISTS(SELECT 1 FROM chat_members WHERE chat_id = @chatId AND user_id = @userId)";
        return await db.ExecuteScalarAsync<bool>(sql, new { chatId, userId }, ct);
    }

    public Task<IReadOnlyList<Guid>> GetMemberIdsAsync(Guid chatId, CancellationToken ct = default) =>
        db.QueryAsync<Guid>("SELECT user_id FROM chat_members WHERE chat_id = @chatId", new { chatId }, ct);

    public async Task<List<ChatParticipantDto>> GetChatParticipantsAsync(Guid chatId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT
                u.id AS UserId,
                u.display_name AS DisplayName,
                u.username AS Username
            FROM chat_members cm
            INNER JOIN users u ON cm.user_id = u.id
            WHERE cm.chat_id = @chatId";

        return [.. await db.QueryAsync<ChatParticipantDto>(sql, new { chatId }, ct)];
    }

    public async Task<List<Guid>> GetAllChatMembersAsync(Guid userId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT DISTINCT cm.user_id
            FROM chat_members cm
            WHERE cm.chat_id IN (
                SELECT chat_id FROM chat_members WHERE user_id = @userId
            ) AND cm.user_id != @userId";

        return [.. await db.QueryAsync<Guid>(sql, new { userId }, ct)];
    }

    private const string ChatListBaseSql = @"
        SELECT
            c.id AS ChatId,
            c.type AS Type,
            c.title AS Title,
            c.created_at AS CreatedAt,

            comp.id AS CompanionId,
            comp.display_name AS CompanionName,
            comp.username AS CompanionUsername,

            -- Непрочитанные: чужие сообщения после указателя по (created_at, id).
            -- Свои не считаются; равное время не путает порядок.
            COALESCE((
                SELECT COUNT(*)
                FROM messages m_unread
                LEFT JOIN messages m_read ON m_read.id = cm_last.last_read_message_id
                WHERE m_unread.chat_id = c.id
                  AND m_unread.is_deleted = false
                  AND m_unread.sender_id <> @userId
                  AND (m_read.id IS NULL
                       OR (m_unread.created_at, m_unread.id) > (m_read.created_at, m_read.id))
            ), 0) AS UnreadCount,

            lm.id AS LastMessageId,
            lm.sender_id AS LastMessageSenderId,
            lm.text AS LastMessageText,
            lm.created_at AS LastMessageCreatedAt,
            sender_u.display_name AS LastMessageSenderName

        FROM chats c
        INNER JOIN chat_members cm ON c.id = cm.chat_id AND cm.user_id = @userId

        LEFT JOIN chat_members cm_last
            ON cm_last.chat_id = c.id AND cm_last.user_id = @userId

        LEFT JOIN LATERAL (
            SELECT u.id, u.display_name, u.username
            FROM chat_members cm2
            INNER JOIN users u ON u.id = cm2.user_id
            WHERE cm2.chat_id = c.id AND cm2.user_id != @userId
            LIMIT 1
        ) comp ON c.type = 'private'

        LEFT JOIN LATERAL (
            SELECT m.id, m.sender_id, m.text, m.created_at
            FROM messages m
            WHERE m.chat_id = c.id AND m.is_deleted = false
            ORDER BY m.created_at DESC, m.id DESC
            LIMIT 1
        ) lm ON TRUE

        LEFT JOIN users sender_u ON sender_u.id = lm.sender_id";

    private static string BuildSearchWhereClause(string? query, string? typeFilter, bool byChatId = false)
    {
        if (!byChatId && string.IsNullOrEmpty(typeFilter) && string.IsNullOrEmpty(query))
            return ""; // No filter — all chats

        var conditions = new List<string>();

        if (byChatId)
            conditions.Add("c.id = @chatId");

        if (typeFilter == "group")
        {
            conditions.Add("c.type = 'group'");
            if (!string.IsNullOrEmpty(query))
                conditions.Add("c.title ILIKE '%' || @query || '%'");
        }
        else if (typeFilter == "private")
        {
            conditions.Add("c.type = 'private'");
            if (!string.IsNullOrEmpty(query))
                conditions.Add("(comp.display_name ILIKE '%' || @query || '%' OR comp.username ILIKE '%' || @query || '%')");
        }
        else
        {
            // No type filter — include both types
            if (!string.IsNullOrEmpty(query))
                conditions.Add("(c.type = 'group' AND c.title ILIKE '%' || @query || '%' OR c.type = 'private' AND (comp.display_name ILIKE '%' || @query || '%' OR comp.username ILIKE '%' || @query || '%'))");
        }

        return conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
    }

    public async Task<List<ChatListResult>> SearchChatsBatchedAsync(
        Guid userId, string? query, string? typeFilter, int? limit, CancellationToken ct = default)
    {
        var whereClause = BuildSearchWhereClause(query, typeFilter);
        var orderBy = "ORDER BY COALESCE(lm.created_at, c.created_at) DESC";
        var limitClause = limit.HasValue ? $" LIMIT {limit.Value}" : "";

        var sql = $"{ChatListBaseSql}\n{whereClause}\n{orderBy}{limitClause}";

        return [.. await db.QueryAsync<ChatListResult>(sql, new { userId, query }, ct)];
    }

    public Task<ChatListResult?> GetChatListItemAsync(Guid chatId, Guid userId, CancellationToken ct = default)
    {
        // Same projection as the chat list — the INNER JOIN on chat_members
        // already scopes the row to the viewer, so a non-member gets null.
        var sql = $"{ChatListBaseSql}\n{BuildSearchWhereClause(null, null, byChatId: true)}\nLIMIT 1";

        return db.QueryFirstOrDefaultAsync<ChatListResult>(
            sql, new { userId, chatId, query = (string?)null }, ct);
    }

    public async Task<int> CountChatsByQueryAsync(
        Guid userId, string? query, string? typeFilter, CancellationToken ct = default)
    {
        var whereClause = BuildSearchWhereClause(query, typeFilter);

        var sql = $@"
            SELECT COUNT(*)
            FROM chats c
            INNER JOIN chat_members cm ON c.id = cm.chat_id AND cm.user_id = @userId
            LEFT JOIN LATERAL (
                SELECT u.display_name, u.username
                FROM chat_members cm2
                INNER JOIN users u ON u.id = cm2.user_id
                WHERE cm2.chat_id = c.id AND cm2.user_id != @userId
                LIMIT 1
            ) comp ON c.type = 'private'
            {whereClause}";

        return await db.ExecuteScalarAsync<int>(sql, new { userId, query }, ct);
    }
}
