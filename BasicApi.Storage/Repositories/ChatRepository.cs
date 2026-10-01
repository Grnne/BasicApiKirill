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
                created_at AS CreatedAt,
                created_by AS CreatedBy,
                updated_at AS UpdatedAt,
                settings::text AS SettingsJson,
                avatar_attachment_id AS AvatarAttachmentId
            FROM chats
            WHERE id = @chatId";

        return db.QueryFirstOrDefaultAsync<Chat>(sql, new { chatId }, ct);
    }

    /// <summary>Pair key for a private chat; participant order does not matter.</summary>
    private const string PrivateKeySql =
        "LEAST(@userId, @otherUserId)::text || ':' || GREATEST(@userId, @otherUserId)::text";

    public Task<Guid?> GetPrivateChatIdAsync(Guid userId, Guid otherUserId, CancellationToken ct = default) =>
        db.QueryFirstOrDefaultAsync<Guid?>(
            $"SELECT id FROM chats WHERE private_key = {PrivateKeySql}", new { userId, otherUserId }, ct);

    public Task<(Guid ChatId, bool Created)> GetOrCreatePrivateChatAsync(
        Guid userId, Guid otherUserId, CancellationToken ct = default) =>
        db.InTransactionAsync(async ct =>
        {
            // The unique index on private_key resolves the race: a parallel insert of the same
            // pair waits for the first one to commit and gets DO NOTHING instead of a second chat.
            var chatId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var inserted = await db.QueryFirstOrDefaultAsync<Guid?>($@"
                INSERT INTO chats (id, title, type, created_at, last_activity_at, private_key)
                VALUES (@chatId, NULL, 'private', @now, @now, {PrivateKeySql})
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

    public Task<(Guid ChatId, bool Created)> GetOrCreateSavedChatAsync(Guid userId, CancellationToken ct = default) =>
        db.InTransactionAsync(async ct =>
        {
            // The same unique private_key as private chats, with its own prefix: one per user,
            // and a concurrent first open waits for the other and finds its chat.
            var chatId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var inserted = await db.QueryFirstOrDefaultAsync<Guid?>(@"
                INSERT INTO chats (id, title, type, created_at, last_activity_at, private_key)
                VALUES (@chatId, NULL, 'saved', @now, @now, 'saved:' || @userId::text)
                ON CONFLICT (private_key) DO NOTHING
                RETURNING id",
                new { chatId, userId, now }, ct);

            if (inserted is not null)
            {
                await db.ExecuteAsync(
                    "INSERT INTO chat_members (chat_id, user_id, joined_at) VALUES (@chatId, @userId, @now)",
                    new { chatId, userId, now }, ct);
                return (chatId, true);
            }

            var existing = await db.QuerySingleAsync<Guid>(
                "SELECT id FROM chats WHERE private_key = 'saved:' || @userId::text", new { userId }, ct);
            return (existing, false);
        }, ct: ct);

    public async Task<bool> IsMemberAsync(Guid chatId, Guid userId, CancellationToken ct = default)
    {
        const string sql = "SELECT EXISTS(SELECT 1 FROM chat_members WHERE chat_id = @chatId AND user_id = @userId)";
        return await db.ExecuteScalarAsync<bool>(sql, new { chatId, userId }, ct);
    }

    public Task<ChatMember?> GetMemberAsync(Guid chatId, Guid userId, CancellationToken ct = default) =>
        db.QueryFirstOrDefaultAsync<ChatMember>($@"
            SELECT {GroupRepository.MemberColumns}
            FROM chat_members cm
            JOIN chats c ON c.id = cm.chat_id
            JOIN users u ON u.id = cm.user_id
            WHERE cm.chat_id = @chatId AND cm.user_id = @userId",
            new { chatId, userId }, ct);

    public async Task<bool> SetMarkedUnreadAsync(Guid chatId, Guid userId, bool markedUnread, CancellationToken ct = default) =>
        await db.ExecuteAsync(@"
            UPDATE chat_members SET marked_unread = @markedUnread
            WHERE chat_id = @chatId AND user_id = @userId AND marked_unread <> @markedUnread",
            new { chatId, userId, markedUnread }, ct) > 0;

    public Task<IReadOnlyList<Guid>> GetMemberIdsAsync(Guid chatId, CancellationToken ct = default) =>
        db.QueryAsync<Guid>("SELECT user_id FROM chat_members WHERE chat_id = @chatId", new { chatId }, ct);

    public async Task<List<ChatParticipantDto>> GetChatParticipantsAsync(Guid chatId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT
                u.id AS UserId,
                u.display_name AS DisplayName,
                u.username AS Username,
                cm.role AS Role,
                u.avatar_attachment_id AS AvatarId
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
            CASE WHEN EXISTS (SELECT 1 FROM user_blocks b WHERE b.blocker_id = comp.id AND b.blocked_id = @userId)
                 THEN NULL ELSE comp.avatar_attachment_id END AS CompanionAvatarId,
            c.avatar_attachment_id AS ChatAvatarId,

            -- Unread: other members' messages after the read pointer. Own messages do not count,
            -- nor do deleted ones and those the user deleted for themselves.
            (
                SELECT COUNT(*)
                FROM messages m_unread
                WHERE m_unread.chat_id = c.id
                  AND m_unread.seq > cm.last_read_seq
                  AND m_unread.deleted_at IS NULL
                  AND m_unread.sender_id <> @userId
                  AND NOT EXISTS (
                      SELECT 1 FROM hidden_messages h WHERE h.user_id = @userId AND h.message_id = m_unread.id)
            ) AS UnreadCount,

            -- Unread mentions of the user: the same rules as for unread messages.
            (
                SELECT COUNT(*)
                FROM message_mentions mm
                INNER JOIN messages m_mention ON m_mention.id = mm.message_id
                WHERE mm.user_id = @userId
                  AND mm.chat_id = c.id
                  AND mm.seq > cm.last_read_seq
                  AND m_mention.deleted_at IS NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM hidden_messages h WHERE h.user_id = @userId AND h.message_id = mm.message_id)
            ) AS UnreadMentionCount,

            cm.last_read_seq AS LastReadSeq,
            cm.pinned_position AS PinnedPosition,
            cm.archived_at AS ArchivedAt,
            cm.muted_until AS MutedUntil,
            cm.marked_unread AS MarkedUnread,
            d.text AS DraftText,
            d.entities::text AS DraftEntitiesJson,
            d.reply_to_message_id AS DraftReplyToMessageId,
            d.updated_at AS DraftUpdatedAt,
            COALESCE(ob.read_seq, 0) AS OutboxReadSeq,
            COALESCE(ob.delivered_seq, 0) AS OutboxDeliveredSeq,
            ob.members > 0 AS HasOthers,

            c.last_activity_at AS LastActivityAt,
            lm.id AS LastMessageId,
            lm.seq AS LastMessageSeq,
            lm.sender_id AS LastMessageSenderId,
            lm.text AS LastMessageText,
            lm.entities::text AS LastMessageEntitiesJson,
            lm.type AS LastMessageType,
            lm.attachments AS LastMessageAttachmentsJson,
            lm.created_at AS LastMessageCreatedAt,
            sender_u.display_name AS LastMessageSenderName,
            COALESCE(lm.sender_id = @userId, false) AS LastMessageIsOwn

        FROM chats c
        INNER JOIN chat_members cm ON c.id = cm.chat_id AND cm.user_id = @userId

        LEFT JOIN LATERAL (
            SELECT u.id, u.display_name, u.username, u.avatar_attachment_id
            FROM chat_members cm2
            INNER JOIN users u ON u.id = cm2.user_id
            WHERE cm2.chat_id = c.id AND cm2.user_id != @userId
            LIMIT 1
        ) comp ON c.type = 'private'

        LEFT JOIN LATERAL (
            SELECT m.id, m.seq, m.sender_id, m.text, m.entities, m.created_at, m.type,
                   " + AttachmentRepository.AttachmentsJsonOf + @"m.id)::text AS attachments
            FROM messages m
            WHERE m.chat_id = c.id
              AND m.deleted_at IS NULL
              AND NOT EXISTS (SELECT 1 FROM hidden_messages h WHERE h.user_id = @userId AND h.message_id = m.id)
            ORDER BY m.seq DESC
            LIMIT 1
        ) lm ON TRUE

        LEFT JOIN users sender_u ON sender_u.id = lm.sender_id
        LEFT JOIN user_drafts d ON d.user_id = @userId AND d.chat_id = c.id

        -- How far the other members got: the status of the user's own messages.
        CROSS JOIN LATERAL (
            SELECT MAX(o.last_read_seq) AS read_seq, MAX(o.last_delivered_seq) AS delivered_seq, COUNT(*) AS members
            FROM chat_members o
            WHERE o.chat_id = c.id AND o.user_id <> @userId
        ) ob";

    private static string BuildSearchWhereClause(string? query, string? typeFilter, bool byChatId = false)
    {
        if (!byChatId && string.IsNullOrEmpty(typeFilter) && string.IsNullOrEmpty(query))
            return "";

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
            if (!string.IsNullOrEmpty(query))
                conditions.Add("(c.type = 'group' AND c.title ILIKE '%' || @query || '%' OR c.type = 'private' AND (comp.display_name ILIKE '%' || @query || '%' OR comp.username ILIKE '%' || @query || '%'))");
        }

        return conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
    }

    public async Task<List<ChatListResult>> SearchChatsBatchedAsync(
        Guid userId, string? query, string? typeFilter, int? limit, CancellationToken ct = default)
    {
        var whereClause = BuildSearchWhereClause(query, typeFilter);
        // Pinned chats on top, in their order; then by activity.
        var orderBy = "ORDER BY cm.pinned_position IS NULL, cm.pinned_position, c.last_activity_at DESC, c.id DESC";
        var limitClause = limit.HasValue ? $" LIMIT {limit.Value}" : "";

        var sql = $"{ChatListBaseSql}\n{whereClause}\n{orderBy}{limitClause}";

        return [.. await db.QueryAsync<ChatListResult>(sql, new { userId, query }, ct)];
    }

    public async Task<IReadOnlyList<ChatListResult>> GetUserChatsPageAsync(
        Guid userId, ChatListCursor? before, int limit, CancellationToken ct = default, bool archived = false,
        bool unpinnedOnly = false)
    {
        // A row comparison matches the order exactly: chats with the same activity go by id.
        var sql = $@"{ChatListBaseSql}
            WHERE (cm.archived_at IS NOT NULL) = @archived
              {(unpinnedOnly ? "AND cm.pinned_position IS NULL" : "")}
              {(before is null ? "" : "AND (c.last_activity_at, c.id) < (@beforeAt, @beforeId)")}
            ORDER BY c.last_activity_at DESC, c.id DESC
            LIMIT @limit";

        return await db.QueryAsync<ChatListResult>(sql, new
        {
            userId,
            query = (string?)null,
            beforeAt = before?.LastActivityAt,
            beforeId = before?.ChatId,
            limit,
            archived
        }, ct);
    }

    public Task<IReadOnlyList<ChatListResult>> GetFolderChatsAsync(
        Guid userId, Folder folder, bool pinned, ChatListCursor? before, int limit, CancellationToken ct = default)
    {
        // The row's columns are unquoted aliases, so outside the inner query they are lower case.
        var sql = $@"
            SELECT * FROM (
                SELECT x.*, fc.pinned_position AS FolderPinnedPosition
                FROM ({ChatListBaseSql}
                      WHERE EXISTS (SELECT 1 FROM folder_chats f WHERE f.folder_id = @folderId AND f.chat_id = c.id)
                         OR cm.archived_at IS NULL
                            AND (@includePrivate AND c.type = 'private' OR @includeGroups AND c.type = 'group')) x
                LEFT JOIN folder_chats fc ON fc.folder_id = @folderId AND fc.chat_id = x.chatid
            ) y
            WHERE (NOT @onlyUnread OR y.unreadcount > 0 OR y.markedunread OR y.folderpinnedposition IS NOT NULL)
              AND y.folderpinnedposition IS {(pinned ? "NOT NULL" : "NULL")}
              {(before is null || pinned ? "" : "AND (y.lastactivityat, y.chatid) < (@beforeAt, @beforeId)")}
            ORDER BY {(pinned ? "y.folderpinnedposition" : "y.lastactivityat DESC, y.chatid DESC")}
            LIMIT @limit";

        return db.QueryAsync<ChatListResult>(sql, new
        {
            userId,
            query = (string?)null,
            folderId = folder.Id,
            includePrivate = folder.IncludePrivate,
            includeGroups = folder.IncludeGroups,
            onlyUnread = folder.OnlyUnread,
            beforeAt = before?.LastActivityAt,
            beforeId = before?.ChatId,
            limit
        }, ct);
    }

    public Task<IReadOnlyList<ChatListResult>> GetPinnedChatsAsync(Guid userId, CancellationToken ct = default) =>
        db.QueryAsync<ChatListResult>($@"{ChatListBaseSql}
            WHERE cm.pinned_position IS NOT NULL AND cm.archived_at IS NULL
            ORDER BY cm.pinned_position, c.id",
            new { userId, query = (string?)null }, ct);

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
