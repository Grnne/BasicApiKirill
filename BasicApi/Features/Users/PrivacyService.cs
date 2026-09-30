using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Users;
using BasicApi.Services;
using BasicApi.Services.Events;
using BasicApi.Storage;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Features.Users;

/// <summary>The user's privacy settings (D11).</summary>
public interface IPrivacyService
{
    Task<PrivacySettingsDto> GetAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Changes the given settings; the user's other devices get <c>PrivacyUpdated</c>. Hiding or
    /// showing last seen takes effect at once: contacts see the user go offline or online.
    /// Errors: 400 <c>INVALID_PRIVACY</c>.
    /// </summary>
    Task<PrivacySettingsDto> UpdateAsync(Guid userId, PrivacySettingsDto changes, CancellationToken ct = default);

    /// <summary>
    /// Blocks or unblocks <paramref name="targetId"/>: in a private chat neither writes to the other,
    /// the blocked one cannot start a chat or add the user to groups, and sees neither the user's
    /// presence nor avatar. Takes effect at once; the user's devices get <c>BlockListChanged</c>.
    /// Errors: 400 <c>INVALID_REQUEST</c> (oneself), 404 <c>USER_NOT_FOUND</c>.
    /// </summary>
    Task SetBlockedAsync(Guid userId, Guid targetId, bool blocked, CancellationToken ct = default);

    /// <summary>Whom the user blocked, the latest first.</summary>
    Task<IReadOnlyList<UserProfileResponseDto>> GetBlockedAsync(Guid userId, CancellationToken ct = default);
}

public sealed class PrivacyService(
    IDbSession db,
    IPrivacyRepository privacy,
    IPresenceService presence,
    IChatEventPublisher events,
    IUserRepository users,
    TimeProvider time) : IPrivacyService
{
    public async Task<PrivacySettingsDto> GetAsync(Guid userId, CancellationToken ct = default) =>
        ToDto(await privacy.GetAsync(userId, ct));

    public async Task<PrivacySettingsDto> UpdateAsync(Guid userId, PrivacySettingsDto changes, CancellationToken ct = default)
    {
        foreach (var level in new[] { changes.LastSeen, changes.Messages, changes.GroupAdd })
        {
            if (level is not null && !PrivacyLevels.IsValid(level))
                throw new BadRequestException("A setting must be everybody, contacts or nobody", "INVALID_PRIVACY");
        }

        var current = await privacy.GetAsync(userId, ct);
        var updated = new UserPrivacy
        {
            UserId = userId,
            LastSeen = changes.LastSeen ?? current.LastSeen,
            Messages = changes.Messages ?? current.Messages,
            GroupAdd = changes.GroupAdd ?? current.GroupAdd,
            UpdatedAt = time.GetUtcNow().UtcDateTime
        };
        if (updated.LastSeen == current.LastSeen && updated.Messages == current.Messages && updated.GroupAdd == current.GroupAdd)
            return ToDto(current);

        var peersBefore = await privacy.GetPresencePeersAsync(userId, ct: ct);
        var dto = await db.InTransactionAsync(async ct =>
        {
            await privacy.SaveAsync(updated, ct);
            var dto = ToDto(updated);
            await events.PrivacyUpdatedAsync(dto, userId, ct);
            return dto;
        }, ct: ct);

        var peersAfter = await privacy.GetPresencePeersAsync(userId, ct: ct);
        await presence.PeersChangedAsync(userId, [.. peersBefore.Except(peersAfter)], [.. peersAfter.Except(peersBefore)], ct);
        return dto;
    }

    public async Task SetBlockedAsync(Guid userId, Guid targetId, bool blocked, CancellationToken ct = default)
    {
        if (userId == targetId)
            throw new BadRequestException("You cannot block yourself", "INVALID_REQUEST");
        if (await users.GetByIdAsync(targetId, ct) is null)
            throw new NotFoundException("User not found", "USER_NOT_FOUND");

        var peersBefore = await privacy.GetPresencePeersAsync(userId, [targetId], ct);
        var changed = await db.InTransactionAsync(async ct =>
        {
            var changed = blocked
                ? await privacy.BlockAsync(userId, targetId, time.GetUtcNow().UtcDateTime, ct)
                : await privacy.UnblockAsync(userId, targetId, ct);
            if (changed)
                await events.BlockListChangedAsync(new BlockListChangedDto { UserId = targetId, Blocked = blocked }, userId, ct);
            return changed;
        }, ct: ct);
        if (!changed)
            return;

        var peersAfter = await privacy.GetPresencePeersAsync(userId, [targetId], ct);
        await presence.PeersChangedAsync(userId, [.. peersBefore.Except(peersAfter)], [.. peersAfter.Except(peersBefore)], ct);
    }

    public async Task<IReadOnlyList<UserProfileResponseDto>> GetBlockedAsync(Guid userId, CancellationToken ct = default) =>
        [.. (await privacy.GetBlockedAsync(userId, ct)).Select(u => new UserProfileResponseDto
        {
            UserId = u.Id,
            Username = u.Username,
            DisplayName = u.DisplayName,
            AvatarId = u.AvatarAttachmentId
        })];

    private static PrivacySettingsDto ToDto(UserPrivacy p) => new()
    {
        LastSeen = p.LastSeen,
        Messages = p.Messages,
        GroupAdd = p.GroupAdd
    };
}
