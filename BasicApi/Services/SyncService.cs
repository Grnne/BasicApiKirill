using System.Data;
using System.Text.Json;
using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Sync;
using BasicApi.Storage;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Services;

/// <summary>
/// Синхронизация после обрыва связи. Клиент держит pts — номер последнего известного
/// ему изменения из своего журнала. После переподключения спрашивает «что после pts»
/// и догоняет пропущенное; если журнал уже не помнит так далеко — берёт снимок.
/// </summary>
public interface ISyncService
{
    Task<SyncStateDto> GetStateAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Ошибки: 400 <c>INVALID_PTS</c> — отрицательный since.</summary>
    Task<SyncDifferenceDto> GetDifferenceAsync(Guid userId, long since, int limit, CancellationToken ct = default);

    /// <summary>
    /// Устройство получило журнал до pts. Ошибки: 400 <c>INVALID_PTS</c> — отрицательный
    /// или больше последнего pts пользователя.
    /// </summary>
    Task AckAsync(Guid userId, Guid sessionFamilyId, long pts, CancellationToken ct = default);
}

public sealed class SyncService(IDbSession db, IUpdateJournal journal, IChatService chats) : ISyncService
{
    public Task<SyncStateDto> GetStateAsync(Guid userId, CancellationToken ct = default) =>
        // Один снимок базы на pts и список чатов: изменение, вошедшее в список, вошло и в pts,
        // и наоборот — ничего не теряется и не учитывается дважды.
        db.InTransactionAsync(async ct => new SyncStateDto
        {
            Pts = await journal.GetPtsAsync(userId, ct),
            Chats = await chats.GetUserChatsAsync(userId, ct)
        }, IsolationLevel.RepeatableRead, ct);

    public async Task<SyncDifferenceDto> GetDifferenceAsync(Guid userId, long since, int limit, CancellationToken ct = default)
    {
        if (since < 0)
            throw InvalidPts();

        var current = await journal.GetPtsAsync(userId, ct);
        if (since > current)
            return new SyncDifferenceDto { Pts = current, SnapshotRequired = true };
        if (since == current)
            return new SyncDifferenceDto { Pts = current };

        var updates = await journal.GetSinceAsync(userId, since, limit, ct);

        // Номера в журнале идут подряд; первый не since + 1 — начало уже вычищено.
        if (updates.Count == 0 || updates[0].Pts != since + 1)
            return new SyncDifferenceDto { Pts = current, SnapshotRequired = true };

        var last = updates[^1].Pts;
        return new SyncDifferenceDto
        {
            Updates = [.. updates.Select(u => new SyncUpdateDto
            {
                Pts = u.Pts,
                Type = u.Type,
                Payload = JsonDocument.Parse(u.Payload).RootElement.Clone(),
                CreatedAt = u.CreatedAt
            })],
            Pts = last,
            HasMore = last < current
        };
    }

    public async Task AckAsync(Guid userId, Guid sessionFamilyId, long pts, CancellationToken ct = default)
    {
        if (pts < 0 || pts > await journal.GetPtsAsync(userId, ct))
            throw InvalidPts();

        await journal.AckAsync(userId, sessionFamilyId, pts, ct);
    }

    private static BadRequestException InvalidPts() => new("pts is out of range", "INVALID_PTS");
}
