using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace BasicApi.Hubs;

/// <summary>
/// Open hub connections by user and sign-in, so that ending a sign-in also drops connections already open.
/// Held in process memory: it sees only this node's connections.
/// </summary>
public sealed class HubConnectionRegistry
{
    private sealed record Entry(Guid UserId, Guid? SessionFamilyId, DateTimeOffset? TokenExpiresAt, HubCallerContext Context);

    private readonly ConcurrentDictionary<string, Entry> _connections = new();

    public void Add(HubCallerContext context, Guid userId, Guid? sessionFamilyId, DateTimeOffset? tokenExpiresAt = null) =>
        _connections[context.ConnectionId] = new Entry(userId, sessionFamilyId, tokenExpiresAt, context);

    public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);

    /// <summary>
    /// Takes the users' open connections out of a hub group — the chat they left or lost — so that
    /// events of that chat stop reaching them. <c>JoinChat</c> will not let them back.
    /// </summary>
    public async Task RemoveFromGroupAsync(
        IGroupManager groups, IEnumerable<Guid> userIds, string groupName, CancellationToken ct = default)
    {
        var users = userIds.ToHashSet();
        foreach (var (connectionId, entry) in _connections)
        {
            if (users.Contains(entry.UserId))
                await groups.RemoveFromGroupAsync(connectionId, groupName, ct);
        }
    }

    /// <summary>Drops all connections of a user; returns their count.</summary>
    public int AbortUser(Guid userId) => Abort(e => e.UserId == userId);

    /// <summary>Drops the user's connections except those of one sign-in (null — all).</summary>
    public int AbortUserExcept(Guid userId, Guid? keepSessionFamilyId) =>
        Abort(e => e.UserId == userId && (keepSessionFamilyId is null || e.SessionFamilyId != keepSessionFamilyId));

    /// <summary>Drops connections opened with tokens of one session chain (one sign-in).</summary>
    public int AbortSessionFamily(Guid sessionFamilyId) => Abort(e => e.SessionFamilyId == sessionFamilyId);

    /// <summary>Sign-ins that currently have open connections.</summary>
    public IReadOnlyCollection<Guid> SessionFamilies() =>
        _connections.Values.Select(e => e.SessionFamilyId).OfType<Guid>().Distinct().ToList();

    /// <summary>Drops the connections of these sign-ins; returns their count.</summary>
    public int AbortSessionFamilies(IReadOnlySet<Guid> sessionFamilyIds) =>
        Abort(e => e.SessionFamilyId is { } id && sessionFamilyIds.Contains(id));

    /// <summary>
    /// Drops connections opened with a token without a sign-in (<c>sid</c>) whose lifetime has
    /// expired: there is nothing to check that they still have access.
    /// </summary>
    public int AbortExpiredWithoutSession(DateTimeOffset now) =>
        Abort(e => e.SessionFamilyId is null && e.TokenExpiresAt <= now);

    private int Abort(Func<Entry, bool> match)
    {
        var aborted = 0;
        foreach (var (connectionId, entry) in _connections)
        {
            if (!match(entry)) continue;
            entry.Context.Abort();
            _connections.TryRemove(connectionId, out _);
            aborted++;
        }
        return aborted;
    }
}
