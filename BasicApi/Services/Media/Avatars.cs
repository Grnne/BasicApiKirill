using BasicApi.Middleware.Exceptions;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;

namespace BasicApi.Services.Media;

public static class Avatars
{
    /// <summary>
    /// A photo the user uploaded themselves, checked and stored: only such may become an avatar.
    /// Errors: 400 <c>INVALID_AVATAR</c> (not a photo), 404 <c>ATTACHMENT_NOT_FOUND</c>.
    /// </summary>
    public static async Task<Attachment> DemandOwnPhotoAsync(
        this IAttachmentRepository attachments, Guid userId, Guid attachmentId, CancellationToken ct = default)
    {
        var file = await attachments.GetOwnAsync(userId, attachmentId, ct);
        if (file is not { StorageState: StorageStates.Stored })
            throw new NotFoundException("No such uploaded file of yours", "ATTACHMENT_NOT_FOUND");
        return file.Kind == AttachmentKinds.Photo
            ? file
            : throw new BadRequestException("An avatar must be a photo", "INVALID_AVATAR");
    }
}
