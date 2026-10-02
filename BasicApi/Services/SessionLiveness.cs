using System.Collections.Concurrent;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Services;

/// <summary>
/// Whether the sign-in behind an access token is still live, asked on every request: otherwise a
/// token outlives its sign-out by its whole lifetime. The answer is kept a few seconds per sign-in;
/// signing out forgets it at once.
/// </summary>
public sealed class SessionLiveness(TimeProvider time)
{
    /// <summary>How long an answer is trusted: the most a revocation that bypasses <see cref="Forget"/> takes.</summary>
    public static readonly TimeSpan Keep = TimeSpan.FromSeconds(15);

    private const int SweepAbove = 10_000;

    private readonly ConcurrentDictionary<Guid, (bool Live, DateTimeOffset CheckedAt)> _known = new();

    // Bumped by every Forget: an answer read from the database before a sign-out is not kept after it.
    private long _generation;

    public async ValueTask<bool> IsLiveAsync(Guid sessionFamilyId, ISessionRepository sessions, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (_known.TryGetValue(sessionFamilyId, out var known) && now - known.CheckedAt < Keep)
            return known.Live;

        var generation = Interlocked.Read(ref _generation);
        var live = await sessions.HasLiveSessionInFamilyAsync(sessionFamilyId, ct);
        if (Interlocked.Read(ref _generation) == generation)
            _known[sessionFamilyId] = (live, now);

        if (_known.Count > SweepAbove)
        {
            foreach (var (id, entry) in _known)
                if (now - entry.CheckedAt >= Keep)
                    _known.TryRemove(id, out _);
        }
        return live;
    }

    /// <summary>One sign-in has ended.</summary>
    public void Forget(Guid sessionFamilyId)
    {
        Interlocked.Increment(ref _generation);
        _known.TryRemove(sessionFamilyId, out _);
    }

    /// <summary>Several sign-ins of a user have ended (sign-out everywhere, a new password): rare, so all answers go.</summary>
    public void ForgetAll()
    {
        Interlocked.Increment(ref _generation);
        _known.Clear();
    }
}
