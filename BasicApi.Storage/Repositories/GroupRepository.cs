using BasicApi.Storage.Dto;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Storage.Repositories;

public sealed class GroupRepository(IDbSession db) : IGroupRepository
{
    internal const string MemberColumns = @"
        cm.chat_id AS ChatId,
        cm.user_id AS UserId,
        c.type AS ChatType,
        cm.role AS Role,
        cm.permissions::text AS PermissionsJson,
        c.settings::text AS SettingsJson,
        cm.joined_at AS JoinedAt,
        u.display_name AS DisplayName,
        u.username AS Username,
        u.avatar_attachment_id AS AvatarId";

    public Task CreateAsync(
        Guid chatId, string title, Guid creatorId, IReadOnlyCollection<Guid> memberIds, DateTime now,
        CancellationToken ct = default) =>
        db.InTransactionAsync(async ct =>
        {
            await db.ExecuteAsync(@"
                INSERT INTO chats (id, title, type, created_at, last_activity_at, created_by, updated_at)
                VALUES (@chatId, @title, 'group', @now, @now, @creatorId, @now)",
                new { chatId, title, creatorId, now }, ct);

            await db.ExecuteAsync(@"
                INSERT INTO chat_members (chat_id, user_id, joined_at, role)
                SELECT @chatId, u, @now, CASE WHEN u = @creatorId THEN 'owner' ELSE 'member' END
                FROM unnest(@userIds) AS u",
                new { chatId, creatorId, now, userIds = memberIds.Append(creatorId).Distinct().ToArray() }, ct);
            return true;
        }, ct: ct);

    public Task<IReadOnlyList<ChatMember>> GetMembersAsync(Guid chatId, CancellationToken ct = default) =>
        db.QueryAsync<ChatMember>($@"
            SELECT {MemberColumns}
            FROM chat_members cm
            JOIN chats c ON c.id = cm.chat_id
            JOIN users u ON u.id = cm.user_id
            WHERE cm.chat_id = @chatId
            ORDER BY CASE cm.role WHEN 'owner' THEN 0 WHEN 'admin' THEN 1 ELSE 2 END, cm.joined_at, cm.user_id",
            new { chatId }, ct);

    public Task<int?> LockAsync(Guid chatId, CancellationToken ct = default) =>
        db.InTransactionAsync(async ct =>
        {
            var found = await db.QueryFirstOrDefaultAsync<Guid?>(
                "SELECT id FROM chats WHERE id = @chatId FOR UPDATE", new { chatId }, ct);
            return found is null
                ? (int?)null
                : await db.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM chat_members WHERE chat_id = @chatId", new { chatId }, ct);
        }, ct: ct);

    public Task<IReadOnlyList<Guid>> AddMembersAsync(
        Guid chatId, IReadOnlyCollection<Guid> userIds, DateTime now, CancellationToken ct = default) =>
        // History before joining is visible but not unread: the pointers start at the chat's last message.
        db.QueryAsync<Guid>(@"
            INSERT INTO chat_members (chat_id, user_id, joined_at, role, last_read_seq, last_delivered_seq, joined_seq)
            SELECT @chatId, u, @now, 'member', c.last_seq, c.last_seq, c.last_seq
            FROM unnest(@userIds) AS u
            CROSS JOIN chats c
            WHERE c.id = @chatId
            ON CONFLICT (chat_id, user_id) DO NOTHING
            RETURNING user_id",
            new { chatId, now, userIds = userIds.Distinct().ToArray() }, ct);

    public async Task<bool> RemoveMemberAsync(Guid chatId, Guid userId, CancellationToken ct = default) =>
        // What the member had read stays read for the authors: the chat keeps it.
        await db.ExecuteScalarAsync<bool>(@"
            WITH gone AS (
                DELETE FROM chat_members WHERE chat_id = @chatId AND user_id = @userId
                RETURNING last_read_seq, last_delivered_seq, joined_seq
            ), kept AS (
                UPDATE chats c
                SET departed_read_seq = GREATEST(c.departed_read_seq,
                        CASE WHEN g.last_read_seq > g.joined_seq THEN g.last_read_seq ELSE 0 END),
                    departed_delivered_seq = GREATEST(c.departed_delivered_seq,
                        CASE WHEN g.last_delivered_seq > g.joined_seq THEN g.last_delivered_seq ELSE 0 END)
                FROM gone g
                WHERE c.id = @chatId
            )
            SELECT EXISTS (SELECT 1 FROM gone)", new { chatId, userId }, ct);

    public Task SetRoleAsync(Guid chatId, Guid userId, string role, CancellationToken ct = default) =>
        db.ExecuteAsync(
            "UPDATE chat_members SET role = @role WHERE chat_id = @chatId AND user_id = @userId",
            new { chatId, userId, role }, ct);

    public Task SetPermissionsAsync(Guid chatId, Guid userId, string? permissionsJson, CancellationToken ct = default) =>
        db.ExecuteAsync(
            "UPDATE chat_members SET permissions = @permissionsJson::jsonb WHERE chat_id = @chatId AND user_id = @userId",
            new { chatId, userId, permissionsJson }, ct);

    public Task UpdateAsync(Guid chatId, string title, string? settingsJson, DateTime now, CancellationToken ct = default) =>
        db.ExecuteAsync(
            "UPDATE chats SET title = @title, settings = @settingsJson::jsonb, updated_at = @now WHERE id = @chatId",
            new { chatId, title, settingsJson, now }, ct);

    public async Task<bool> SetAvatarAsync(Guid chatId, Guid? attachmentId, DateTime now, CancellationToken ct = default) =>
        await db.ExecuteAsync(@"
            UPDATE chats SET avatar_attachment_id = @attachmentId, updated_at = @now
            WHERE id = @chatId AND avatar_attachment_id IS DISTINCT FROM @attachmentId",
            new { chatId, attachmentId, now }, ct) > 0;

    public Task DeleteAsync(Guid chatId, CancellationToken ct = default) =>
        db.ExecuteAsync("DELETE FROM chats WHERE id = @chatId", new { chatId }, ct);

    public Task AppendAuditAsync(ChatAuditEntry entry, CancellationToken ct = default) =>
        db.ExecuteAsync(@"
            INSERT INTO chat_audit_log (chat_id, actor_id, action, target_user_id, data, created_at)
            VALUES (@ChatId, @ActorId, @Action, @TargetUserId, @DataJson::jsonb, @CreatedAt)",
            entry, ct);

    public Task<IReadOnlyList<ChatAuditEntry>> GetAuditAsync(
        Guid chatId, long? beforeId, int limit, CancellationToken ct = default) =>
        db.QueryAsync<ChatAuditEntry>($@"
            SELECT id AS Id, chat_id AS ChatId, actor_id AS ActorId, action AS Action,
                   target_user_id AS TargetUserId, data::text AS DataJson, created_at AS CreatedAt
            FROM chat_audit_log
            WHERE chat_id = @chatId {(beforeId is null ? "" : "AND id < @beforeId")}
            ORDER BY id DESC
            LIMIT @limit",
            new { chatId, beforeId, limit }, ct);
}
