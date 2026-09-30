using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Storage.Repositories;

public sealed class PrivacyRepository(IDbSession db) : IPrivacyRepository
{
    /// <summary>A user's level for one setting; no row — everybody.</summary>
    private static string Level(string column, string userId) =>
        $"COALESCE((SELECT p.{column} FROM user_privacy p WHERE p.user_id = {userId}), 'everybody')";

    /// <summary>A block between the two, either way.</summary>
    private static string Blocked(string a, string b) => $@"
        EXISTS (SELECT 1 FROM user_blocks b
                WHERE (b.blocker_id = {a} AND b.blocked_id = {b}) OR (b.blocker_id = {b} AND b.blocked_id = {a}))";

    public async Task<UserPrivacy> GetAsync(Guid userId, CancellationToken ct = default) =>
        await db.QueryFirstOrDefaultAsync<UserPrivacy>(@"
            SELECT user_id AS UserId, last_seen AS LastSeen, messages AS Messages, group_add AS GroupAdd,
                   updated_at AS UpdatedAt
            FROM user_privacy WHERE user_id = @userId",
            new { userId }, ct)
        ?? new UserPrivacy { UserId = userId };

    public Task SaveAsync(UserPrivacy privacy, CancellationToken ct = default) =>
        db.ExecuteAsync(@"
            INSERT INTO user_privacy (user_id, last_seen, messages, group_add, updated_at)
            VALUES (@UserId, @LastSeen, @Messages, @GroupAdd, @UpdatedAt)
            ON CONFLICT (user_id) DO UPDATE
            SET last_seen = EXCLUDED.last_seen, messages = EXCLUDED.messages,
                group_add = EXCLUDED.group_add, updated_at = EXCLUDED.updated_at",
            privacy, ct);

    public Task<IReadOnlyList<Guid>> GetPresencePeersAsync(
        Guid userId, IReadOnlyCollection<Guid>? among = null, CancellationToken ct = default) =>
        // "Everybody" and "contacts" are the same here: presence is only ever shown to contacts.
        // Hiding one's own also hides the others' — the relation is mutual, as in Telegram.
        db.QueryAsync<Guid>($@"
            SELECT DISTINCT cm.user_id
            FROM chat_members mine
            JOIN chat_members cm ON cm.chat_id = mine.chat_id AND cm.user_id <> @userId
            WHERE mine.user_id = @userId
              {(among is null ? "" : "AND cm.user_id = ANY(@among)")}
              AND {Level("last_seen", "@userId")} <> 'nobody'
              AND {Level("last_seen", "cm.user_id")} <> 'nobody'
              AND NOT {Blocked("@userId", "cm.user_id")}",
            new { userId, among = among?.ToArray() }, ct);

    public Task<bool> ShareChatAsync(Guid userId, Guid otherId, CancellationToken ct = default) =>
        db.ExecuteScalarAsync<bool>(@"
            SELECT EXISTS (
                SELECT 1 FROM chat_members a
                JOIN chat_members b ON b.chat_id = a.chat_id AND b.user_id = @otherId
                WHERE a.user_id = @userId)",
            new { userId, otherId }, ct);

    public Task<bool> IsBlockedAsync(Guid blockerId, Guid blockedId, CancellationToken ct = default) =>
        db.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM user_blocks WHERE blocker_id = @blockerId AND blocked_id = @blockedId)",
            new { blockerId, blockedId }, ct);

    public async Task<IReadOnlySet<Guid>> GetBlockersAsync(
        Guid userId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default) =>
        userIds.Count == 0
            ? new HashSet<Guid>()
            : (await db.QueryAsync<Guid>(
                "SELECT blocker_id FROM user_blocks WHERE blocked_id = @userId AND blocker_id = ANY(@userIds)",
                new { userId, userIds = userIds.ToArray() }, ct)).ToHashSet();

    public async Task<IReadOnlySet<Guid>> GetBlockedAmongAsync(
        Guid blockerId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default) =>
        userIds.Count == 0
            ? new HashSet<Guid>()
            : (await db.QueryAsync<Guid>(
                "SELECT blocked_id FROM user_blocks WHERE blocker_id = @blockerId AND blocked_id = ANY(@userIds)",
                new { blockerId, userIds = userIds.ToArray() }, ct)).ToHashSet();

    public async Task<bool> BlockAsync(Guid blockerId, Guid blockedId, DateTime now, CancellationToken ct = default) =>
        await db.ExecuteAsync(@"
            INSERT INTO user_blocks (blocker_id, blocked_id, created_at) VALUES (@blockerId, @blockedId, @now)
            ON CONFLICT DO NOTHING",
            new { blockerId, blockedId, now }, ct) > 0;

    public async Task<bool> UnblockAsync(Guid blockerId, Guid blockedId, CancellationToken ct = default) =>
        await db.ExecuteAsync(
            "DELETE FROM user_blocks WHERE blocker_id = @blockerId AND blocked_id = @blockedId",
            new { blockerId, blockedId }, ct) > 0;

    public Task<IReadOnlyList<User>> GetBlockedAsync(Guid blockerId, CancellationToken ct = default) =>
        db.QueryAsync<User>(@"
            SELECT u.id AS Id, u.username AS Username, u.display_name AS DisplayName,
                   u.avatar_attachment_id AS AvatarAttachmentId, u.is_active AS IsActive
            FROM user_blocks b JOIN users u ON u.id = b.blocked_id
            WHERE b.blocker_id = @blockerId
            ORDER BY b.created_at DESC, u.id",
            new { blockerId }, ct);

    public Task<IReadOnlyList<Guid>> GetGroupAddRefusalsAsync(
        Guid adderId, IReadOnlyCollection<Guid> candidateIds, CancellationToken ct = default) =>
        db.QueryAsync<Guid>($@"
            SELECT c.id
            FROM unnest(@candidateIds) AS c(id)
            WHERE {Level("group_add", "c.id")} = 'nobody'
               OR {Level("group_add", "c.id")} = 'contacts' AND NOT EXISTS (
                      SELECT 1 FROM chat_members a
                      JOIN chat_members b ON b.chat_id = a.chat_id AND b.user_id = c.id
                      WHERE a.user_id = @adderId)
               OR EXISTS (SELECT 1 FROM user_blocks b WHERE b.blocker_id = c.id AND b.blocked_id = @adderId)",
            new { adderId, candidateIds = candidateIds.Distinct().ToArray() }, ct);
}
