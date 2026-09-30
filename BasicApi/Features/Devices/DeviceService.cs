using BasicApi.Hubs;
using BasicApi.Middleware.Exceptions;
using BasicApi.Storage;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Features.Devices;

/// <summary>The user's devices: each sign-in is one, until it ends.</summary>
public interface IDeviceService
{
    Task<DeviceListDto> GetAllAsync(Guid userId, Guid? currentDeviceId, CancellationToken ct = default);

    /// <summary>
    /// Signs the device out: its refresh token stops working and its hub connections close.
    /// Errors: 404 <c>DEVICE_NOT_FOUND</c> — not the caller's or already signed out.
    /// </summary>
    Task SignOutAsync(Guid userId, Guid deviceId, CancellationToken ct = default);
}

public sealed class DeviceService(
    IDbSession db,
    IDeviceRepository devices,
    ISessionRepository sessions,
    HubConnectionRegistry connections,
    TimeProvider time) : IDeviceService
{
    public async Task<DeviceListDto> GetAllAsync(Guid userId, Guid? currentDeviceId, CancellationToken ct = default) =>
        new()
        {
            Items = [.. (await devices.GetLiveAsync(userId, ct)).Select(d => new DeviceDto
            {
                Id = d.Id,
                IsCurrent = d.Id == currentDeviceId,
                SignedInAt = d.SignedInAt,
                LastActiveAt = d.LastActiveAt,
                UserAgent = d.UserAgent,
                PushEnabled = d.PushEnabled
            })]
        };

    public async Task SignOutAsync(Guid userId, Guid deviceId, CancellationToken ct = default)
    {
        await db.InTransactionAsync(async ct =>
        {
            if (!await devices.IsLiveAsync(userId, deviceId, ct))
                throw new NotFoundException("Device not found", "DEVICE_NOT_FOUND");
            await sessions.RevokeFamilyAsync(deviceId, time.GetUtcNow().UtcDateTime, ct);
            await devices.DeleteAsync(deviceId, ct);
            return true;
        }, ct: ct);
        connections.AbortSessionFamily(deviceId);
    }
}
