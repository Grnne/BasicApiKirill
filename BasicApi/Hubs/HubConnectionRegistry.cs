using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace BasicApi.Hubs;

/// <summary>
/// Живые соединения хаба с привязкой к пользователю и сессии. Нужен, чтобы
/// отзыв доступа (logout, logout-all) обрывал и уже открытые соединения:
/// сами по себе они живут часами, независимо от судьбы refresh-токена.
/// Остальные концы входа (отзыв цепочки при краже токена, истечение сессии)
/// ловит <see cref="HubSessionMonitor"/>.
///
/// В памяти процесса — как и presence. При горизонтальном масштабировании
/// понадобится рассылка команды «оборвать» между узлами (backplane).
/// </summary>
public sealed class HubConnectionRegistry
{
    private sealed record Entry(Guid UserId, Guid? SessionFamilyId, DateTimeOffset? TokenExpiresAt, HubCallerContext Context);

    private readonly ConcurrentDictionary<string, Entry> _connections = new();

    public void Add(HubCallerContext context, Guid userId, Guid? sessionFamilyId, DateTimeOffset? tokenExpiresAt = null) =>
        _connections[context.ConnectionId] = new Entry(userId, sessionFamilyId, tokenExpiresAt, context);

    public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);

    /// <summary>Обрывает все соединения пользователя; возвращает их число.</summary>
    public int AbortUser(Guid userId) => Abort(e => e.UserId == userId);

    /// <summary>Обрывает соединения, открытые с токенами одной цепочки сессий (одного входа).</summary>
    public int AbortSessionFamily(Guid sessionFamilyId) => Abort(e => e.SessionFamilyId == sessionFamilyId);

    /// <summary>Входы, с которых сейчас открыты соединения.</summary>
    public IReadOnlyCollection<Guid> SessionFamilies() =>
        _connections.Values.Select(e => e.SessionFamilyId).OfType<Guid>().Distinct().ToList();

    /// <summary>Обрывает соединения этих входов; возвращает их число.</summary>
    public int AbortSessionFamilies(IReadOnlySet<Guid> sessionFamilyIds) =>
        Abort(e => e.SessionFamilyId is { } id && sessionFamilyIds.Contains(id));

    /// <summary>
    /// Обрывает соединения, открытые с токеном без входа (<c>sid</c>), чей срок истёк:
    /// проверить, что доступ у них ещё есть, нечем.
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
