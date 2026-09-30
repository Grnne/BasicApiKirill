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
    /// Deletes up to <paramref name="batchSize"/> devices whose sign-in has ended in any way
    /// (logout, expiry, revocation on token theft); returns how many.
    /// </summary>
    Task<int> DeleteDeadAsync(int batchSize, CancellationToken ct = default);
}

public sealed class Device
{
    public Guid Id { get; set; }

    /// <summary>When the user signed in on it.</summary>
    public DateTime SignedInAt { get; set; }

    /// <summary>The last token refresh: an open client does it every few minutes.</summary>
    public DateTime LastActiveAt { get; set; }

    public string? UserAgent { get; set; }
}
