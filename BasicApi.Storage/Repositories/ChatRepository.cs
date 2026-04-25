using System.Data;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Dapper;

namespace BasicApi.Storage.Repositories;

public class ChatRepository(IDbConnection connection) : IChatRepository
{
        public async Task<IEnumerable<Chat>> GetUserChatsAsync(Guid userId)
    {
        const string sql = @"
            SELECT DISTINCT 
                c.id AS Id, 
                c.title AS Title, 
                c.type AS Type, 
                c.created_at AS CreatedAt
            FROM chats c
            INNER JOIN chat_members cm ON c.id = cm.chat_id
            WHERE cm.user_id = @userId
            ORDER BY c.created_at DESC";

        return await connection.QueryAsync<Chat>(sql, new { userId });
    }

    public async Task<List<ChatListResult>> GetUserChatsBatchedAsync(Guid userId)
    {
        const string sql = @"
            SELECT
                c.id AS ChatId,
                c.type AS Type,
                c.title AS Title,
                c.created_at AS CreatedAt,

                -- Companion name (for private chats)
                comp.display_name AS CompanionName,

                -- Unread count
                COALESCE((
                    SELECT COUNT(*)
                    FROM messages m_unread
                    WHERE m_unread.chat_id = c.id
                      AND m_unread.is_deleted = false
                      AND m_unread.created_at > COALESCE(
                          (SELECT created_at FROM messages WHERE id = cm_last.last_read_message_id),
                          '1970-01-01'::timestamp
                      )
                ), 0) AS UnreadCount,

                -- Last message fields via LATERAL
                lm.id AS LastMessageId,
                lm.sender_id AS LastMessageSenderId,
                lm.text AS LastMessageText,
                lm.created_at AS LastMessageCreatedAt,
                sender_u.display_name AS LastMessageSenderName

            FROM chats c
            INNER JOIN chat_members cm ON c.id = cm.chat_id AND cm.user_id = @userId

            -- Last read message for unread count
            LEFT JOIN chat_members cm_last
                ON cm_last.chat_id = c.id AND cm_last.user_id = @userId

            -- Companion for private chats
            LEFT JOIN LATERAL (
                SELECT u.display_name
                FROM chat_members cm2
                INNER JOIN users u ON u.id = cm2.user_id
                WHERE cm2.chat_id = c.id AND cm2.user_id != @userId
                LIMIT 1
            ) comp ON c.type = 'private'

            -- Last message via LATERAL (single row)
            LEFT JOIN LATERAL (
                SELECT m.id, m.sender_id, m.text, m.created_at
                FROM messages m
                WHERE m.chat_id = c.id AND m.is_deleted = false
                ORDER BY m.created_at DESC, m.id DESC
                LIMIT 1
            ) lm ON TRUE

            -- Sender name for last message
            LEFT JOIN users sender_u ON sender_u.id = lm.sender_id

            ORDER BY COALESCE(lm.created_at, c.created_at) DESC";

        return (await connection.QueryAsync<ChatListResult>(sql, new { userId })).AsList();
    }

    public async Task<Chat?> GetByIdAsync(Guid chatId)
    {
        const string sql = @"
            SELECT 
                id AS Id, 
                title AS Title, 
                type AS Type, 
                created_at AS CreatedAt
            FROM chats 
            WHERE id = @chatId";

        return await connection.QueryFirstOrDefaultAsync<Chat>(sql, new { chatId });
    }

    public async Task<Chat?> GetPrivateChatAsync(Guid userId1, Guid userId2)
    {
        const string sql = @"
            SELECT 
                c.id AS Id, 
                c.title AS Title, 
                c.type AS Type, 
                c.created_at AS CreatedAt
            FROM chats c
            INNER JOIN chat_members cm1 ON c.id = cm1.chat_id
            INNER JOIN chat_members cm2 ON c.id = cm2.chat_id
            WHERE c.type = 'private' 
                AND cm1.user_id = @userId1 
                AND cm2.user_id = @userId2";

        return await connection.QueryFirstOrDefaultAsync<Chat>(sql, new { userId1, userId2 });
    }

    public async Task<Guid> CreateAsync(Chat chat, Guid[] memberIds)
    {
        using var transaction = connection.BeginTransaction();

        try
        {
            const string insertChatSql = @"
                INSERT INTO chats (id, title, type, created_at) 
                VALUES (@Id, @Title, @Type, @CreatedAt)";

            await connection.ExecuteAsync(insertChatSql, chat, transaction);

            const string insertMemberSql = @"
                INSERT INTO chat_members (chat_id, user_id, joined_at) 
                VALUES (@ChatId, @UserId, @JoinedAt)";

            foreach (var userId in memberIds)
            {
                await connection.ExecuteAsync(insertMemberSql, new
                {
                    ChatId = chat.Id,
                    UserId = userId,
                    JoinedAt = DateTime.UtcNow
                }, transaction);
            }

            transaction.Commit();
            return chat.Id;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<bool> IsMemberAsync(Guid chatId, Guid userId)
    {
        const string sql = "SELECT COUNT(1) FROM chat_members WHERE chat_id = @chatId AND user_id = @userId";
        return await connection.ExecuteScalarAsync<bool>(sql, new { chatId, userId });
    }

        public async Task<int> GetUnreadCountAsync(Guid chatId, Guid userId)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM messages m
            WHERE m.chat_id = @chatId
              AND m.is_deleted = false
              AND m.created_at > COALESCE(
                  (SELECT created_at FROM messages WHERE id = (
                      SELECT last_read_message_id FROM chat_members
                      WHERE chat_id = @chatId AND user_id = @userId
                  )),
                  '1970-01-01'::timestamp
              )";

        return await connection.ExecuteScalarAsync<int>(sql, new { chatId, userId });
    }

    public async Task<string?> GetCompanionNameAsync(Guid chatId, Guid userId)
    {
        const string sql = @"
            SELECT u.display_name 
            FROM chat_members cm
            INNER JOIN users u ON cm.user_id = u.id
            WHERE cm.chat_id = @chatId AND cm.user_id != @userId";

        return await connection.QueryFirstOrDefaultAsync<string?>(sql, new { chatId, userId });
    }

    public async Task<string> GetUserNameAsync(Guid userId)
    {
        const string sql = "SELECT display_name FROM users WHERE id = @userId";
        var name = await connection.QueryFirstOrDefaultAsync<string>(sql, new { userId });
        return name ?? "Unknown";
    }

    public async Task<List<ChatParticipantDto>> GetChatParticipantsAsync(Guid chatId)
    {
        const string sql = @"
            SELECT 
                u.id AS UserId, 
                u.display_name AS DisplayName, 
                u.username AS Username
            FROM chat_members cm
            INNER JOIN users u ON cm.user_id = u.id
            WHERE cm.chat_id = @chatId";

        var result = await connection.QueryAsync<ChatParticipantDto>(sql, new { chatId });
        return [.. result];
    }
}