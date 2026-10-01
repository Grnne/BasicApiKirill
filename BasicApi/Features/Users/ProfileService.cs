using BasicApi.Features.Media;
using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Users;
using BasicApi.Services;
using BasicApi.Services.Events;
using BasicApi.Storage;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Features.Users;

/// <summary>Changes to the user's own profile; the others learn of them by <c>UserUpdated</c>.</summary>
public interface IProfileService
{
    /// <summary>
    /// Changes the name shown to others; they learn of it by <c>UserUpdated</c>.
    /// Errors: 400 <c>INVALID_DISPLAY_NAME</c>.
    /// </summary>
    Task<OwnProfileResponseDto> UpdateAsync(Guid userId, UpdateProfileDto changes, CancellationToken ct = default);

    /// <summary>
    /// Sets the avatar to a photo the user uploaded, or clears it (<paramref name="attachmentId"/>
    /// null). Errors: 400 <c>INVALID_AVATAR</c>, 404 <c>ATTACHMENT_NOT_FOUND</c>.
    /// </summary>
    Task<OwnProfileResponseDto> SetAvatarAsync(Guid userId, Guid? attachmentId, CancellationToken ct = default);
}

public sealed class ProfileService(
    IDbSession db,
    IUserRepository users,
    IAttachmentRepository attachments,
    IMembershipService membership,
    IPrivacyRepository privacy,
    IChatEventPublisher events) : IProfileService
{
    public const int MaxDisplayNameLength = 100;

    public async Task<OwnProfileResponseDto> UpdateAsync(Guid userId, UpdateProfileDto changes, CancellationToken ct = default)
    {
        var name = changes.DisplayName?.Trim();
        if (name is not null && (name.Length == 0 || name.Length > MaxDisplayNameLength || name.Any(char.IsControl)))
            throw new BadRequestException(
                $"The name must be 1 to {MaxDisplayNameLength} characters", "INVALID_DISPLAY_NAME");

        return await db.InTransactionAsync(async ct =>
        {
            var changed = name is not null && await users.SetDisplayNameAsync(userId, name, ct);
            var user = await users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User not found", "USER_NOT_FOUND");
            if (changed)
                await AnnounceAsync(user, ct);
            return UserService.OwnProfile(user);
        }, ct: ct);
    }

    public async Task<OwnProfileResponseDto> SetAvatarAsync(Guid userId, Guid? attachmentId, CancellationToken ct = default)
    {
        if (attachmentId is { } id)
            await attachments.DemandOwnPhotoAsync(userId, id, ct);

        return await db.InTransactionAsync(async ct =>
        {
            var changed = await users.SetAvatarAsync(userId, attachmentId, ct);
            var user = await users.GetByIdAsync(userId, ct) ?? throw new NotFoundException("User not found", "USER_NOT_FOUND");
            if (changed)
                await AnnounceAsync(user, ct);
            return UserService.OwnProfile(user);
        }, ct: ct);
    }

    /// <summary>
    /// The new public profile to the user's devices and to everyone who shares a chat with them —
    /// except whom the user blocked.
    /// </summary>
    private async Task AnnounceAsync(User user, CancellationToken ct)
    {
        var contacts = await membership.GetContactIdsAsync(user.Id, ct);
        var blocked = await privacy.GetBlockedAmongAsync(user.Id, contacts, ct);
        var recipients = contacts.Where(id => !blocked.Contains(id)).ToList();
        await events.UserUpdatedAsync(new UserUpdatedDto
        {
            UserId = user.Id,
            DisplayName = user.DisplayName,
            Username = user.Username,
            AvatarId = user.AvatarAttachmentId
        }, [user.Id, .. recipients], ct);
    }
}
