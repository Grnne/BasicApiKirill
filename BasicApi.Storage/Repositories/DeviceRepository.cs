using BasicApi.Storage.Interfaces;

namespace BasicApi.Storage.Repositories;

public sealed class DeviceRepository(IDbSession db) : IDeviceRepository
{
    /// <summary>The live head of the device's session chain: the session its tokens come from now.</summary>
    private const string LiveHead = @"
        SELECT s.created_at, s.user_agent FROM sessions s
        WHERE s.family_id = d.id AND s.revoked_at IS NULL AND s.expires_at > now()
        ORDER BY s.created_at DESC
        LIMIT 1";

    public Task<IReadOnlyList<Device>> GetLiveAsync(Guid userId, CancellationToken ct = default) =>
        db.QueryAsync<Device>($@"
            SELECT d.id AS Id, d.created_at AS SignedInAt, h.created_at AS LastActiveAt, h.user_agent AS UserAgent,
                   d.push_endpoint IS NOT NULL AS PushEnabled
            FROM devices d
            CROSS JOIN LATERAL ({LiveHead}) h
            WHERE d.user_id = @userId
            ORDER BY h.created_at DESC, d.id",
            new { userId }, ct);

    public async Task<bool> IsLiveAsync(Guid userId, Guid deviceId, CancellationToken ct = default) =>
        await db.ExecuteScalarAsync<bool>($@"
            SELECT EXISTS (
                SELECT 1 FROM devices d
                WHERE d.id = @deviceId AND d.user_id = @userId AND EXISTS ({LiveHead})
            )",
            new { userId, deviceId }, ct);

    public Task DeleteAsync(Guid deviceId, CancellationToken ct = default) =>
        db.ExecuteAsync("DELETE FROM devices WHERE id = @deviceId", new { deviceId }, ct);

    public Task DeleteAllExceptAsync(Guid userId, Guid? keepDeviceId, CancellationToken ct = default) =>
        db.ExecuteAsync(
            "DELETE FROM devices WHERE user_id = @userId AND id IS DISTINCT FROM @keepDeviceId",
            new { userId, keepDeviceId }, ct);

    public async Task SetPushAsync(Guid deviceId, DevicePush push, DateTime now, CancellationToken ct = default)
    {
        await db.ExecuteAsync(@"
            UPDATE devices SET push_endpoint = NULL, push_p256dh = NULL, push_auth = NULL, push_updated_at = @now
            WHERE push_endpoint = @Endpoint AND id <> @deviceId",
            new { deviceId, push.Endpoint, now }, ct);
        await db.ExecuteAsync(@"
            UPDATE devices SET push_endpoint = @Endpoint, push_p256dh = @P256dh, push_auth = @Auth, push_updated_at = @now
            WHERE id = @deviceId",
            new { deviceId, push.Endpoint, push.P256dh, push.Auth, now }, ct);
    }

    public Task ClearPushAsync(Guid userId, Guid deviceId, CancellationToken ct = default) =>
        db.ExecuteAsync(@"
            UPDATE devices SET push_endpoint = NULL, push_p256dh = NULL, push_auth = NULL, push_updated_at = now()
            WHERE id = @deviceId AND user_id = @userId AND push_endpoint IS NOT NULL",
            new { userId, deviceId }, ct);

    public Task ClearPushByEndpointAsync(string endpoint, CancellationToken ct = default) =>
        db.ExecuteAsync(@"
            UPDATE devices SET push_endpoint = NULL, push_p256dh = NULL, push_auth = NULL, push_updated_at = now()
            WHERE push_endpoint = @endpoint",
            new { endpoint }, ct);

    public Task<IReadOnlyList<PushTarget>> GetPushTargetsAsync(Guid chatId, Guid senderId, CancellationToken ct = default) =>
        db.QueryAsync<PushTarget>($@"
            SELECT d.id AS DeviceId, d.user_id AS UserId, d.push_endpoint AS Endpoint,
                   d.push_p256dh AS P256dh, d.push_auth AS Auth, c.type AS ChatType, c.title AS ChatTitle
            FROM chat_members cm
            JOIN chats c ON c.id = cm.chat_id
            JOIN devices d ON d.user_id = cm.user_id AND d.push_endpoint IS NOT NULL
            WHERE cm.chat_id = @chatId AND cm.user_id <> @senderId
              AND (cm.muted_until IS NULL OR cm.muted_until <= now())
              AND NOT EXISTS (
                  SELECT 1 FROM user_blocks b WHERE b.blocker_id = cm.user_id AND b.blocked_id = @senderId)
              AND EXISTS ({LiveHead})",
            new { chatId, senderId }, ct);

    public Task<int> DeleteDeadAsync(int batchSize, CancellationToken ct = default) =>
        db.ExecuteAsync($@"
            DELETE FROM devices WHERE id IN (
                SELECT d.id FROM devices d
                WHERE NOT EXISTS ({LiveHead})
                LIMIT @batchSize
            )",
            new { batchSize }, ct);
}
