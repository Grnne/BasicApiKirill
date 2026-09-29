using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace BasicApi.Hubs;

/// <summary>
/// Живые соединения хаба с привязкой к пользователю и сессии. Нужен, чтобы
/// отзыв доступа (logout, logout-all) обрывал и уже открытые соединения:
/// сами по себе они живут часами, независимо от судьбы refresh-токена.
///
/// В памяти процесса — как и presence. При горизонтальном масштабировании
/// понадобится рассылка команды «оборвать» между узлами (backplane).
/// </summary>
public sealed class HubConnectionRegistry
{
    private sealed record Entry(Guid UserId, Guid? SessionFamilyId, HubCallerContext Context);

    private readonly ConcurrentDictionary<string, Entry> _connections = new();

    public void Add(HubCallerContext context, Guid userId, Guid? sessionFamilyId) =>
        _connections[context.ConnectionId] = new Entry(userId, sessionFamilyId, context);

    public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);

    /// <summary>Обрывает все соединения пользователя; возвращает их число.</summary>
    public int AbortUser(Guid userId) => Abort(e => e.UserId == userId);

    /// <summary>Обрывает соединения, открытые с токенами одной цепочки сессий (одного входа).</summary>
    public int AbortSessionFamily(Guid sessionFamilyId) => Abort(e => e.SessionFamilyId == sessionFamilyId);

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
