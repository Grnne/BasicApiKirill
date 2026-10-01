using System.Buffers.Text;
using BasicApi.Middleware.Exceptions;
using BasicApi.Storage;
using BasicApi.Storage.Interfaces;
using Microsoft.Extensions.Options;

namespace BasicApi.Features.Push;

/// <summary>Push subscriptions: one per device, the device being the sign-in the request comes from.</summary>
public interface IPushService
{
    PushConfigDto GetConfig();

    /// <summary>
    /// Subscribes the device; a subscription the same browser gave another sign-in moves here.
    /// Errors: 400 <c>INVALID_SUBSCRIPTION</c>, 404 <c>DEVICE_NOT_FOUND</c> (the sign-in has ended),
    /// 503 <c>PUSH_UNAVAILABLE</c>.
    /// </summary>
    Task SubscribeAsync(Guid userId, Guid? deviceId, PushSubscriptionDto subscription, CancellationToken ct = default);

    Task UnsubscribeAsync(Guid userId, Guid? deviceId, CancellationToken ct = default);
}

public sealed class PushService(
    IDbSession db,
    IDeviceRepository devices,
    IOptions<PushOptions> options,
    TimeProvider time) : IPushService
{
    public const int MaxEndpointLength = 2048;

    private readonly PushOptions _options = options.Value;

    public PushConfigDto GetConfig() => new()
    {
        Enabled = _options.IsConfigured,
        VapidPublicKey = _options.IsConfigured ? _options.VapidPublicKey : null
    };

    public async Task SubscribeAsync(Guid userId, Guid? deviceId, PushSubscriptionDto subscription, CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
            throw new ServiceUnavailableException("Push notifications are not configured on this server", "PUSH_UNAVAILABLE");

        var endpoint = subscription.Endpoint;
        var p256dh = subscription.Keys?.P256dh;
        var auth = subscription.Keys?.Auth;
        if (endpoint is null || endpoint.Length > MaxEndpointLength
            || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || !_options.IsAllowedEndpoint(uri)
            || !IsKey(p256dh, 65, 0x04) || !IsKey(auth, 16))
            throw new BadRequestException(
                "Not a push subscription of a known push service", "INVALID_SUBSCRIPTION");

        await db.InTransactionAsync(async ct =>
        {
            if (deviceId is null || !await devices.IsLiveAsync(userId, deviceId.Value, ct))
                throw DeviceNotFound();
            await devices.SetPushAsync(deviceId.Value,
                new DevicePush(endpoint, p256dh!, auth!), time.GetUtcNow().UtcDateTime, ct);
            return true;
        }, ct: ct);
    }

    public async Task UnsubscribeAsync(Guid userId, Guid? deviceId, CancellationToken ct = default)
    {
        if (deviceId is not null)
            await devices.ClearPushAsync(userId, deviceId.Value, ct);
    }

    private static bool IsKey(string? value, int length, byte? first = null)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128)
            return false;
        try
        {
            var bytes = Base64Url.DecodeFromChars(value);
            return bytes.Length == length && (first is null || bytes[0] == first);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static NotFoundException DeviceNotFound() => new("Device not found", "DEVICE_NOT_FOUND");
}
