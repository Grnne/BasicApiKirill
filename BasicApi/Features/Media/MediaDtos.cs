namespace BasicApi.Features.Media;

/// <summary>What the client is about to upload.</summary>
public class CreateUploadDto
{
    /// <summary><c>photo</c>, <c>video</c>, <c>file</c> or <c>voice</c>.</summary>
    public string? Kind { get; set; }

    /// <summary>The name to show; path parts and control characters are dropped.</summary>
    public string? FileName { get; set; }

    /// <summary>
    /// The type the upload will carry in <c>Content-Type</c>. Only for display: the server finds the
    /// real type in the file itself.
    /// </summary>
    public string? MimeType { get; set; }

    /// <summary>Size in bytes; checked against the limits before the upload starts.</summary>
    public long Size { get; set; }

    /// <summary>Video: the frame size, as the client measured it.</summary>
    public int? Width { get; set; }
    public int? Height { get; set; }

    /// <summary>Video and voice: the length in milliseconds; required for voice.</summary>
    public int? DurationMs { get; set; }

    /// <summary>Voice: the loudness curve the client drew, up to 256 values 0–255.</summary>
    public List<int>? Waveform { get; set; }

    /// <summary>Video: the client will upload a frame for the preview too.</summary>
    public bool WithThumbnail { get; set; }
}

/// <summary>Where to upload: <c>PUT</c> the file to <see cref="UploadUrl"/>, then complete the upload.</summary>
public class UploadTicketDto
{
    public Guid AttachmentId { get; set; }

    /// <summary>A signed link; the request must carry exactly <see cref="ContentType"/>.</summary>
    public string UploadUrl { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    /// <summary>For a video with <c>withThumbnail</c>: where to <c>PUT</c> a JPEG or PNG frame.</summary>
    public string? ThumbnailUploadUrl { get; set; }

    /// <summary>The <c>Content-Type</c> for <see cref="ThumbnailUploadUrl"/>.</summary>
    public string? ThumbnailContentType { get; set; }

    /// <summary>After this the links stop working; an upload not completed by then is removed later.</summary>
    public DateTime ExpiresAt { get; set; }
}

public class MediaLinksRequestDto
{
    public const int MaxIds = 100;

    public List<Guid> AttachmentIds { get; set; } = [];
}

public class MediaLinkDto
{
    public Guid AttachmentId { get; set; }

    /// <summary>The file itself; null when it has expired.</summary>
    public string? Url { get; set; }

    /// <summary>The preview; null when there is none.</summary>
    public string? ThumbnailUrl { get; set; }

    public DateTime ExpiresAt { get; set; }
}

public class MediaLinksDto
{
    /// <summary>One per file the caller may see; the others are left out.</summary>
    public List<MediaLinkDto> Items { get; set; } = [];
}
