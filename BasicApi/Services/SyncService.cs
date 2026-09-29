using System.Data;
using System.Text.Json;
using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Sync;
using BasicApi.Storage;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Services;

/// <summary>
/// Sync after a connection drop. The client keeps pts — the number of the last change
/// it knows of from its journal. After reconnecting it asks "what is after pts"
/// and catches up on what it missed; if the journal no longer remembers that far — takes a snapshot.
/// </summary>
public interface ISyncService
{
    Task<SyncStateDto> GetStateAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Errors: 400 <c>INVALID_PTS</c> — negative since.</summary>
    Task<SyncDifferenceDto> GetDifferenceAsync(Guid userId, long since, int limit, CancellationToken ct = default);

    /// <summary>
    /// The device has received the journal up to pts. Errors: 400 <c>INVALID_PTS</c> — negative
    /// or greater than the user's last pts.
    /// </summary>
    Task AckAsync(Guid userId, Guid sessionFamilyId, long pts, CancellationToken ct = default);
}

public sealed class SyncService(IDbSession db, IUpdateJournal journal, IChatService chats) : ISyncService
{
    public Task<SyncStateDto> GetStateAsync(Guid userId, CancellationToken ct = default) =>
        // One database snapshot for pts and the chat list: a change that made it into the list also made it into pts,
        // and vice versa — nothing is lost or counted twice.
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

        // Journal numbers are consecutive; a first one that is not since + 1 means the start was already purged.
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
