using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Users;
using BasicApi.Services.Events;
using BasicApi.Services.Media;
using BasicApi.Storage;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Services;

/// <summary>Changes to the user's own profile; the others learn of them by <c>UserUpdated</c>.</summary>
public interface IProfileService
{
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
    IChatEventPublisher events) : IProfileService
{
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

    /// <summary>The new public profile to the user's devices and to everyone who shares a chat with them.</summary>
    private async Task AnnounceAsync(User user, CancellationToken ct)
    {
        var recipients = await membership.GetContactIdsAsync(user.Id, ct);
        await events.UserUpdatedAsync(new UserUpdatedDto
        {
            UserId = user.Id,
            DisplayName = user.DisplayName,
            Username = user.Username,
            AvatarId = user.AvatarAttachmentId
        }, [user.Id, .. recipients], ct);
    }
}
