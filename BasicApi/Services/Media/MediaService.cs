using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BasicApi.Middleware.Exceptions;
using BasicApi.Models.Dto.Media;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Microsoft.Extensions.Options;

namespace BasicApi.Services.Media;

public interface IMediaService
{
    /// <summary>
    /// Starts an upload: a pending file and a link to put it at.
    /// Errors: 400 <c>INVALID_MEDIA</c>/<c>FILE_TOO_LARGE</c>, 409 <c>TOO_MANY_UPLOADS</c>,
    /// 503 <c>MEDIA_UNAVAILABLE</c>.
    /// </summary>
    Task<UploadTicketDto> CreateUploadAsync(Guid userId, CreateUploadDto request, CancellationToken ct = default);

    /// <summary>
    /// Checks what was uploaded — size, content, picture size — makes the preview and makes the
    /// file usable. Repeating it returns the same file.
    /// Errors: 400 <c>UPLOAD_INCOMPLETE</c>/<c>FILE_TOO_LARGE</c>/<c>INVALID_MEDIA</c>,
    /// 404 <c>UPLOAD_NOT_FOUND</c>, 503 <c>MEDIA_UNAVAILABLE</c>.
    /// </summary>
    Task<AttachmentDto> CompleteUploadAsync(Guid userId, Guid attachmentId, CancellationToken ct = default);

    /// <summary>Download links for the files the user may see; the others are left out.</summary>
    Task<MediaLinksDto> GetLinksAsync(Guid userId, IReadOnlyList<Guid> attachmentIds, CancellationToken ct = default);
}

public sealed partial class MediaService(
    IAttachmentRepository attachments,
    IObjectStorage storage,
    IOptions<MediaOptions> options,
    TimeProvider time) : IMediaService
{
    public const int MaxFileNameLength = 255;
    public const int MaxWaveformLength = 256;
    public const int MaxDimension = 20_000;
    public const int MaxDurationMs = 24 * 60 * 60 * 1000;

    /// <summary>The largest video frame the client may send for the preview.</summary>
    public const long MaxThumbnailSize = 2 * 1024 * 1024;

    /// <summary>Decoding pictures is CPU- and memory-hungry: a few at a time per process.</summary>
    private static readonly SemaphoreSlim ProcessingGate = new(2, 2);

    private readonly MediaOptions _options = options.Value;

    public static string OriginalKey(Guid id) => $"o/{id:N}";
    public static string ThumbnailKey(Guid id) => $"t/{id:N}";

    /// <summary>Where the client puts a video frame; the server turns it into the preview.</summary>
    public static string ClientThumbnailKey(Guid id) => $"c/{id:N}";

    public async Task<UploadTicketDto> CreateUploadAsync(
        Guid userId, CreateUploadDto request, CancellationToken ct = default)
    {
        var kind = request.Kind?.Trim().ToLowerInvariant();
        if (!AttachmentKinds.IsValid(kind))
            throw Invalid("kind must be photo, video, file or voice");
        if (request.Size <= 0)
            throw Invalid("size must be positive");
        var limit = kind == AttachmentKinds.Photo ? _options.MaxPhotoSize : _options.MaxFileSize;
        if (request.Size > limit)
            throw TooLarge(limit);

        var (width, height, duration, waveform) = Metadata(kind!, request);
        if (await attachments.CountPendingAsync(userId, ct) >= _options.MaxPendingUploads)
            throw new ConflictException(
                $"At most {_options.MaxPendingUploads} uploads may be unfinished at once", "TOO_MANY_UPLOADS");

        var now = time.GetUtcNow().UtcDateTime;
        var lifetime = TimeSpan.FromMinutes(_options.UploadUrlMinutes);
        var contentType = ContentTypeOf(request.MimeType);
        var attachment = new Attachment
        {
            Id = Guid.NewGuid(),
            OwnerId = userId,
            Kind = kind!,
            FileName = CleanFileName(request.FileName),
            Mime = contentType,
            Size = request.Size,
            Width = width,
            Height = height,
            DurationMs = duration,
            Waveform = waveform,
            CreatedAt = now
        };
        attachment.StorageKey = OriginalKey(attachment.Id);

        // Links first: a storage that is off or unreachable fails the request before a row is left.
        var uploadUrl = await storage.PresignPutAsync(attachment.StorageKey, contentType, lifetime);
        var thumbnailUrl = kind == AttachmentKinds.Video && request.WithThumbnail
            ? await storage.PresignPutAsync(ClientThumbnailKey(attachment.Id), MediaSniffer.Jpeg, lifetime)
            : null;
        await attachments.CreateAsync(attachment, ct);

        return new UploadTicketDto
        {
            AttachmentId = attachment.Id,
            UploadUrl = uploadUrl.ToString(),
            ContentType = contentType,
            ThumbnailUploadUrl = thumbnailUrl?.ToString(),
            ThumbnailContentType = thumbnailUrl is null ? null : MediaSniffer.Jpeg,
            ExpiresAt = now.Add(lifetime)
        };
    }

    public async Task<AttachmentDto> CompleteUploadAsync(Guid userId, Guid attachmentId, CancellationToken ct = default)
    {
        var attachment = await attachments.GetOwnAsync(userId, attachmentId, ct)
            ?? throw new NotFoundException("No such upload", "UPLOAD_NOT_FOUND");
        if (attachment.StorageState != StorageStates.Pending)
            return ToDto(attachment);

        var size = await storage.GetSizeAsync(attachment.StorageKey, ct)
            ?? throw new BadRequestException("The file has not been uploaded yet", "UPLOAD_INCOMPLETE");
        var limit = attachment.Kind == AttachmentKinds.Photo ? _options.MaxPhotoSize : _options.MaxFileSize;
        if (size > limit)
            await RejectAsync(attachment, TooLarge(limit));

        await ProcessingGate.WaitAsync(ct);
        try
        {
            await CheckAsync(attachment, ct);
        }
        finally
        {
            ProcessingGate.Release();
        }

        attachment.StoredAt = time.GetUtcNow().UtcDateTime;
        if (!await attachments.MarkStoredAsync(attachment, ct))
        {
            // A concurrent completion got there first, or the upload was swept as stale.
            return await attachments.GetOwnAsync(userId, attachmentId, ct) is { StorageState: not StorageStates.Pending } done
                ? ToDto(done)
                : throw new NotFoundException("No such upload", "UPLOAD_NOT_FOUND");
        }
        attachment.StorageState = StorageStates.Stored;
        return ToDto(attachment);
    }

    /// <summary>
    /// Reads the object once: its hash, real size and type; for a photo — the preview. A video frame
    /// from the client becomes the video's preview.
    /// </summary>
    private async Task CheckAsync(Attachment attachment, CancellationToken ct)
    {
        var keepBytes = attachment.Kind == AttachmentKinds.Photo;
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var copy = keepBytes ? new MemoryStream() : null;
        var head = new byte[MediaSniffer.HeaderLength];
        var headLength = 0;
        long size = 0;

        await using (var stream = await storage.OpenReadAsync(attachment.StorageKey, ct))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                sha.AppendData(buffer, 0, read);
                if (headLength < head.Length)
                {
                    var take = Math.Min(read, head.Length - headLength);
                    Array.Copy(buffer, 0, head, headLength, take);
                    headLength += take;
                }
                copy?.Write(buffer, 0, read);
                size += read;
                if (size > (keepBytes ? _options.MaxPhotoSize : _options.MaxFileSize))
                    await RejectAsync(attachment, TooLarge(keepBytes ? _options.MaxPhotoSize : _options.MaxFileSize));
            }
        }
        if (size == 0)
            await RejectAsync(attachment, Invalid("the file is empty"));

        var mime = MediaSniffer.Sniff(head.AsSpan(0, headLength));
        attachment.Size = size;
        attachment.Sha256 = sha.GetHashAndReset();

        switch (attachment.Kind)
        {
            case AttachmentKinds.Photo:
                var preview = MediaSniffer.IsImage(mime)
                    ? ImagePreviews.Make(copy!.ToArray(), _options.PreviewSize, _options.MaxPhotoPixels)
                    : null;
                if (preview is null)
                    await RejectAsync(attachment, Invalid("the file is not a JPEG, PNG, GIF or WebP picture of an allowed size"));
                attachment.Mime = mime;
                attachment.Width = preview!.Width;
                attachment.Height = preview.Height;
                attachment.ThumbnailKey = await PutThumbnailAsync(attachment.Id, preview.Jpeg, ct);
                break;

            case AttachmentKinds.Video:
                if (!MediaSniffer.IsVideo(mime))
                    await RejectAsync(attachment, Invalid("the file is not an MP4, MOV or WebM video"));
                attachment.Mime = mime;
                attachment.ThumbnailKey = await VideoThumbnailAsync(attachment.Id, ct);
                break;

            case AttachmentKinds.Voice:
                if (!MediaSniffer.IsAudio(mime))
                    await RejectAsync(attachment, Invalid("the file is not an Ogg, WebM, MP4 or MP3 recording"));
                attachment.Mime = mime == MediaSniffer.Mp4 ? MediaSniffer.M4a : mime == MediaSniffer.Webm ? "audio/webm" : mime;
                break;

            default:
                // A file is only ever downloaded, never shown: its type is kept for the icon.
                attachment.Mime = mime == MediaSniffer.Unknown ? attachment.Mime : mime;
                break;
        }
    }

    private async Task<string?> VideoThumbnailAsync(Guid id, CancellationToken ct)
    {
        var key = ClientThumbnailKey(id);
        if (await storage.GetSizeAsync(key, ct) is not { } size)
            return null;
        try
        {
            if (size > MaxThumbnailSize)
                return null;
            using var buffer = new MemoryStream();
            await using (var stream = await storage.OpenReadAsync(key, ct))
                await stream.CopyToAsync(buffer, ct);
            // The frame is decoded and re-encoded like a photo: what the client sent is never served.
            var preview = ImagePreviews.Make(buffer.ToArray(), _options.PreviewSize, _options.MaxPhotoPixels);
            return preview is null ? null : await PutThumbnailAsync(id, preview.Jpeg, ct);
        }
        finally
        {
            await storage.DeleteAsync([key], ct);
        }
    }

    private async Task<string> PutThumbnailAsync(Guid id, byte[] jpeg, CancellationToken ct)
    {
        var key = ThumbnailKey(id);
        using var content = new MemoryStream(jpeg);
        await storage.PutAsync(key, content, MediaSniffer.Jpeg, ct);
        return key;
    }

    /// <summary>A bad upload is removed at once, with its row: the client starts over.</summary>
    private async Task RejectAsync(Attachment attachment, Exception error)
    {
        await storage.DeleteAsync([attachment.StorageKey, ClientThumbnailKey(attachment.Id)], CancellationToken.None);
        await attachments.DeleteAsync([attachment.Id], CancellationToken.None);
        throw error;
    }

    public async Task<MediaLinksDto> GetLinksAsync(
        Guid userId, IReadOnlyList<Guid> attachmentIds, CancellationToken ct = default)
    {
        if (attachmentIds.Count == 0 || attachmentIds.Count > MediaLinksRequestDto.MaxIds)
            throw new BadRequestException(
                $"attachmentIds must have 1 to {MediaLinksRequestDto.MaxIds} ids", "INVALID_REQUEST");

        var lifetime = TimeSpan.FromMinutes(_options.DownloadUrlMinutes);
        var expiresAt = time.GetUtcNow().UtcDateTime.Add(lifetime);
        var items = new List<MediaLinkDto>();
        foreach (var a in await attachments.GetAccessibleAsync(userId, attachmentIds, ct))
        {
            items.Add(new MediaLinkDto
            {
                AttachmentId = a.Id,
                Url = a.StorageState == StorageStates.Stored
                    ? (await storage.PresignGetAsync(a.StorageKey, lifetime, ServedType(a), Disposition(a))).ToString()
                    : null,
                ThumbnailUrl = a.ThumbnailKey is { } thumb
                    ? (await storage.PresignGetAsync(thumb, lifetime, MediaSniffer.Jpeg, "inline")).ToString()
                    : null,
                ExpiresAt = expiresAt
            });
        }
        return new MediaLinksDto { Items = items };
    }

    /// <summary>
    /// How the storage serves a file (R32): photos, videos and voice — inline with the type the server
    /// found (none of them can run script); anything else — a download of unknown type, so an
    /// uploaded HTML or SVG never opens as a page.
    /// </summary>
    public static string ServedType(Attachment a) =>
        a.Kind == AttachmentKinds.File ? MediaSniffer.Unknown : a.Mime;

    public static string Disposition(Attachment a)
    {
        if (a.Kind != AttachmentKinds.File)
            return "inline";
        var ascii = AsciiName().Replace(a.FileName, "_");
        return $"attachment; filename=\"{ascii}\"; filename*=UTF-8''{Uri.EscapeDataString(a.FileName)}";
    }

    public static AttachmentDto ToDto(Attachment a) => new()
    {
        Id = a.Id,
        Kind = a.Kind,
        FileName = a.FileName,
        MimeType = a.Mime,
        Size = a.Size,
        Width = a.Width,
        Height = a.Height,
        DurationMs = a.DurationMs,
        Waveform = a.Waveform?.Select(b => (int)b).ToList(),
        HasThumbnail = a.ThumbnailKey is not null,
        State = a.StorageState
    };

    private static (int? Width, int? Height, int? Duration, byte[]? Waveform) Metadata(string kind, CreateUploadDto r)
    {
        switch (kind)
        {
            case AttachmentKinds.Video:
                if (r.Width is < 1 or > MaxDimension || r.Height is < 1 or > MaxDimension)
                    throw Invalid($"width and height must be 1 to {MaxDimension}");
                if (r.DurationMs is < 0 or > MaxDurationMs)
                    throw Invalid("durationMs is out of range");
                return (r.Width, r.Height, r.DurationMs, null);

            case AttachmentKinds.Voice:
                if (r.DurationMs is not (> 0 and <= MaxDurationMs))
                    throw Invalid("durationMs is required for a voice message");
                if (r.Waveform is { } wave && (wave.Count > MaxWaveformLength || wave.Any(v => v is < 0 or > 255)))
                    throw Invalid($"waveform must have at most {MaxWaveformLength} values 0-255");
                return (null, null, r.DurationMs, r.Waveform?.Select(v => (byte)v).ToArray());

            default:
                // A photo's size is measured by the server; nothing else has any.
                return (null, null, null, null);
        }
    }

    /// <summary>The name only: no folders, no control characters, at most 255 characters.</summary>
    public static string CleanFileName(string? name)
    {
        var clean = new StringBuilder();
        foreach (var c in Path.GetFileName((name ?? string.Empty).Replace('\\', '/')))
        {
            if (!char.IsControl(c) && c is not ('/' or '\\' or '"'))
                clean.Append(c);
        }
        var result = clean.ToString().Trim();
        if (result.Length > MaxFileNameLength)
            result = result[..MaxFileNameLength];
        return result.Length == 0 || result is "." or ".." ? "file" : result;
    }

    /// <summary>A well-formed <c>type/subtype</c>, or <c>application/octet-stream</c>.</summary>
    private static string ContentTypeOf(string? mime) =>
        mime is not null && MimePattern().IsMatch(mime) ? mime.ToLowerInvariant() : MediaSniffer.Unknown;

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9!#$&^_.+-]{0,63}/[A-Za-z0-9][A-Za-z0-9!#$&^_.+-]{0,63}$")]
    private static partial Regex MimePattern();

    [GeneratedRegex(@"[^\x20-\x7E]|[""\\]")]
    private static partial Regex AsciiName();

    private static BadRequestException Invalid(string message) => new(message, "INVALID_MEDIA");

    private static BadRequestException TooLarge(long limit) =>
        new($"The file is larger than {limit / 1024 / 1024} MB", "FILE_TOO_LARGE");
}
