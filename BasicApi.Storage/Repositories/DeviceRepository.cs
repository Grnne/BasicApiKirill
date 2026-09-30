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
            SELECT d.id AS Id, d.created_at AS SignedInAt, h.created_at AS LastActiveAt, h.user_agent AS UserAgent
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

    public Task<int> DeleteDeadAsync(int batchSize, CancellationToken ct = default) =>
        db.ExecuteAsync($@"
            DELETE FROM devices WHERE id IN (
                SELECT d.id FROM devices d
                WHERE NOT EXISTS ({LiveHead})
                LIMIT @batchSize
            )",
            new { batchSize }, ct);
}
