namespace BasicApi.Storage.Interfaces;

/// <summary>The user's devices: sign-ins that are still open (a live session in the chain).</summary>
public interface IDeviceRepository
{
    /// <summary>The user's live devices, the most recently active first.</summary>
    Task<IReadOnlyList<Device>> GetLiveAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Whether the device is the user's and its sign-in is still open.</summary>
    Task<bool> IsLiveAsync(Guid userId, Guid deviceId, CancellationToken ct = default);

    Task DeleteAsync(Guid deviceId, CancellationToken ct = default);

    /// <summary>Deletes all the user's devices except one (null — all of them).</summary>
    Task DeleteAllExceptAsync(Guid userId, Guid? keepDeviceId, CancellationToken ct = default);

    /// <summary>
    /// Gives the device this push subscription, taking it from any other device that had it:
    /// a browser's subscription belongs to whoever signed in there last. Call in a transaction.
    /// </summary>
    Task SetPushAsync(Guid deviceId, DevicePush push, DateTime now, CancellationToken ct = default);

    Task ClearPushAsync(Guid userId, Guid deviceId, CancellationToken ct = default);

    /// <summary>Removes a subscription the push service no longer knows.</summary>
    Task ClearPushByEndpointAsync(string endpoint, CancellationToken ct = default);

    /// <summary>
    /// Where to push a new message of the chat: the subscribed devices, with an open sign-in, of the
    /// members other than the sender who have not muted the chat and have not blocked the sender;
    /// with <paramref name="recipientId"/> — of that member only.
    /// </summary>
    Task<IReadOnlyList<PushTarget>> GetPushTargetsAsync(
        Guid chatId, Guid senderId, Guid? recipientId = null, CancellationToken ct = default);

    /// <summary>
    /// Deletes up to <paramref name="batchSize"/> devices whose sign-in has ended in any way
    /// (logout, expiry, revocation on token theft); returns how many.
    /// </summary>
    Task<int> DeleteDeadAsync(int batchSize, CancellationToken ct = default);
}

public sealed class Device
{
    public Guid Id { get; set; }

    public DateTime SignedInAt { get; set; }

    /// <summary>The last token refresh: an open client does it every few minutes.</summary>
    public DateTime LastActiveAt { get; set; }

    public string? UserAgent { get; set; }

    public bool PushEnabled { get; set; }
}

/// <summary>A WebPush subscription: where to deliver and the keys to encrypt for (base64url).</summary>
public sealed record DevicePush(string Endpoint, string P256dh, string Auth);

public sealed class PushTarget
{
    public Guid DeviceId { get; set; }
    public Guid UserId { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
    public string ChatType { get; set; } = string.Empty;
    public string? ChatTitle { get; set; }

    public DevicePush Push => new(Endpoint, P256dh, Auth);
}
