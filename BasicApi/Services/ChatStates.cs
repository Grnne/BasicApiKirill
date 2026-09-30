using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Chat;
using BasicApi.Services.Events;
using BasicApi.Storage;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Services;

/// <summary>How the user keeps chats: pinned, archived, muted (D9). The other devices follow by events.</summary>
public interface IChatStateService
{
    /// <summary>
    /// Pins the chat on top of the list, or unpins it; a pinned chat leaves the archive.
    /// Errors: 400 <c>TOO_MANY_PINNED</c>, 403 <c>NOT_A_MEMBER</c>.
    /// </summary>
    Task<PinnedChatsDto> SetPinnedAsync(Guid userId, Guid chatId, bool pinned, CancellationToken ct = default);

    /// <summary>Reorders the pinned chats: exactly those, in a new order. Errors: 400 <c>INVALID_REQUEST</c>.</summary>
    Task<PinnedChatsDto> ReorderPinnedAsync(Guid userId, IReadOnlyList<Guid> chatIds, CancellationToken ct = default);

    /// <summary>Moves the chat to the archive or back; an archived chat is unpinned. Errors: 403 <c>NOT_A_MEMBER</c>.</summary>
    Task<ChatStateDto> SetArchivedAsync(Guid userId, Guid chatId, bool archived, CancellationToken ct = default);

    /// <summary>
    /// Mutes the chat until the moment or for good, or unmutes it.
    /// Errors: 400 <c>INVALID_REQUEST</c> (a moment in the past), 403 <c>NOT_A_MEMBER</c>.
    /// </summary>
    Task<ChatStateDto> SetMutedAsync(Guid userId, Guid chatId, bool muted, DateTime? until, CancellationToken ct = default);

    /// <summary>After a new message: the chat leaves the archive of members who did not mute it.</summary>
    Task UnarchiveOnMessageAsync(Guid chatId, Guid senderId, CancellationToken ct = default);
}

public static class ChatStates
{
    /// <summary>At most this many chats are pinned in the main list.</summary>
    public const int MaxPinned = 10;

    /// <summary>"Muted for good" is kept as a moment that never comes.</summary>
    public static readonly DateTime Forever = new(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc);

    public static bool IsMuted(DateTime? mutedUntil, DateTime now) => mutedUntil > now;

    /// <summary>What clients see: the moment, or null when not muted or muted for good.</summary>
    public static DateTime? ShownUntil(DateTime? mutedUntil, DateTime now) =>
        IsMuted(mutedUntil, now) && mutedUntil < Forever ? mutedUntil : null;
}

public sealed class ChatStateService(
    IDbSession db,
    IChatStateRepository states,
    IChatPolicy policy,
    IChatEventPublisher events,
    TimeProvider time) : IChatStateService
{
    public async Task<PinnedChatsDto> SetPinnedAsync(Guid userId, Guid chatId, bool pinned, CancellationToken ct = default)
    {
        await policy.DemandReadAsync(userId, chatId, ct);
        return await db.InTransactionAsync(async ct =>
        {
            await states.LockUserAsync(userId, ct);
            var current = await states.GetPinnedAsync(userId, ct);
            if (current.Contains(chatId) == pinned)
                return new PinnedChatsDto { ChatIds = [.. current] };
            if (pinned && current.Count >= ChatStates.MaxPinned)
                throw new BadRequestException($"At most {ChatStates.MaxPinned} chats can be pinned", "TOO_MANY_PINNED");

            // A newly pinned chat goes on top, as in Telegram.
            List<Guid> next = pinned ? [chatId, .. current] : [.. current.Where(id => id != chatId)];
            await states.SetPinnedAsync(userId, next, ct);
            if (pinned && await states.SetArchivedAsync(chatId, userId, null, ct))
                await events.ChatStateChangedAsync(await StateAsync(chatId, userId, ct), [userId], ct);
            var dto = new PinnedChatsDto { ChatIds = next };
            await events.PinnedChatsChangedAsync(dto, userId, ct);
            return dto;
        }, ct: ct);
    }

    public async Task<PinnedChatsDto> ReorderPinnedAsync(Guid userId, IReadOnlyList<Guid> chatIds, CancellationToken ct = default) =>
        await db.InTransactionAsync(async ct =>
        {
            await states.LockUserAsync(userId, ct);
            var current = await states.GetPinnedAsync(userId, ct);
            if (chatIds.Count != current.Count || chatIds.Distinct().Count() != chatIds.Count || !current.All(chatIds.Contains))
                throw new BadRequestException("chatIds must be exactly the pinned chats, in the new order", "INVALID_REQUEST");
            var dto = new PinnedChatsDto { ChatIds = [.. chatIds] };
            if (current.SequenceEqual(chatIds))
                return dto;
            await states.SetPinnedAsync(userId, chatIds, ct);
            await events.PinnedChatsChangedAsync(dto, userId, ct);
            return dto;
        }, ct: ct);

    public async Task<ChatStateDto> SetArchivedAsync(Guid userId, Guid chatId, bool archived, CancellationToken ct = default)
    {
        await policy.DemandReadAsync(userId, chatId, ct);
        return await db.InTransactionAsync(async ct =>
        {
            await states.LockUserAsync(userId, ct);
            if (!await states.SetArchivedAsync(chatId, userId, archived ? time.GetUtcNow().UtcDateTime : null, ct))
                return await StateAsync(chatId, userId, ct);

            // The archive keeps no pins: an archived chat leaves the pinned ones.
            var pinned = await states.GetPinnedAsync(userId, ct);
            if (archived && pinned.Contains(chatId))
            {
                List<Guid> next = [.. pinned.Where(id => id != chatId)];
                await states.SetPinnedAsync(userId, next, ct);
                await events.PinnedChatsChangedAsync(new PinnedChatsDto { ChatIds = next }, userId, ct);
            }
            var state = await StateAsync(chatId, userId, ct);
            await events.ChatStateChangedAsync(state, [userId], ct);
            return state;
        }, ct: ct);
    }

    public async Task<ChatStateDto> SetMutedAsync(
        Guid userId, Guid chatId, bool muted, DateTime? until, CancellationToken ct = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var utcUntil = until?.ToUniversalTime();
        if (muted && utcUntil <= now)
            throw new BadRequestException("until must be in the future", "INVALID_REQUEST");
        await policy.DemandReadAsync(userId, chatId, ct);

        return await db.InTransactionAsync(async ct =>
        {
            var changed = await states.SetMutedUntilAsync(chatId, userId, muted ? utcUntil ?? ChatStates.Forever : null, ct);
            var state = await StateAsync(chatId, userId, ct);
            if (changed)
                await events.ChatStateChangedAsync(state, [userId], ct);
            return state;
        }, ct: ct);
    }

    public async Task UnarchiveOnMessageAsync(Guid chatId, Guid senderId, CancellationToken ct = default)
    {
        var back = await states.UnarchiveOnMessageAsync(chatId, senderId, time.GetUtcNow().UtcDateTime, ct);
        foreach (var userId in back)
            await events.ChatStateChangedAsync(await StateAsync(chatId, userId, ct), [userId], ct);
    }

    private async Task<ChatStateDto> StateAsync(Guid chatId, Guid userId, CancellationToken ct)
    {
        var state = await states.GetAsync(chatId, userId, ct) ?? new ChatState();
        var now = time.GetUtcNow().UtcDateTime;
        return new ChatStateDto
        {
            ChatId = chatId,
            Archived = state.ArchivedAt is not null,
            IsMuted = ChatStates.IsMuted(state.MutedUntil, now),
            MutedUntil = ChatStates.ShownUntil(state.MutedUntil, now)
        };
    }
}
