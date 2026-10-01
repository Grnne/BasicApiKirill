namespace BasicApi.Features.Media;

/// <summary>
/// Where files are kept: any S3-compatible storage, section <c>Storage</c>. Without an
/// <see cref="Endpoint"/> media is off and its endpoints answer 503 <c>MEDIA_UNAVAILABLE</c>.
/// </summary>
public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>How the server reaches the storage, e.g. <c>http://seaweedfs:8333</c>.</summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// How clients reach it: upload and download links point here. In production — the site itself
    /// (<c>https://chat.example.com</c>), where the reverse proxy passes <c>/{Bucket}/</c> to the
    /// storage. Empty — the same as <see cref="Endpoint"/>.
    /// </summary>
    public string? PublicUrl { get; set; }

    public string Bucket { get; set; } = "media";
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
    public string Region { get; set; } = "us-east-1";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Endpoint);

    /// <summary>The origin clients load files from, when it is not the site itself; null otherwise.</summary>
    public string? PublicOrigin =>
        IsConfigured && Uri.TryCreate(string.IsNullOrWhiteSpace(PublicUrl) ? Endpoint : PublicUrl, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority)
            : null;
}

/// <summary>Instance-wide file rules, section <c>Media</c>.</summary>
public sealed class MediaOptions
{
    public const string Section = "Media";

    /// <summary>The largest file one may upload.</summary>
    public int MaxFileSizeMb { get; set; } = 100;

    /// <summary>The largest photo: the server decodes it to make the preview.</summary>
    public int MaxPhotoSizeMb { get; set; } = 20;

    /// <summary>A photo with more pixels is refused: decoding it would eat the memory.</summary>
    public long MaxPhotoPixels { get; set; } = 50_000_000;

    /// <summary>The longer side of a preview.</summary>
    public int PreviewSize { get; set; } = 640;

    /// <summary>How long an upload link works.</summary>
    public int UploadUrlMinutes { get; set; } = 30;

    /// <summary>How long a download link works.</summary>
    public int DownloadUrlMinutes { get; set; } = 60;

    /// <summary>An upload not finished within this time is removed.</summary>
    public int PendingUploadHours { get; set; } = 24;

    /// <summary>How many unfinished uploads one user may have at once.</summary>
    public int MaxPendingUploads { get; set; } = 50;

    /// <summary>How often stale uploads, unused files and expired originals are swept.</summary>
    public int CleanupIntervalMinutes { get; set; } = 60;

    /// <summary>
    /// A file nothing points to (never sent, or its messages deleted for everyone) is removed after
    /// this long: time enough to send an upload, or to take it back.
    /// </summary>
    public int UnusedFileHours { get; set; } = 24;

    /// <summary>
    /// Retention policy: originals older than this many days are removed, previews are kept
    /// (the file becomes <c>expired</c>). 0 — keep forever, the default. Avatars are kept whole.
    /// </summary>
    public int RetentionDays { get; set; }

    public long MaxFileSize => MaxFileSizeMb * 1024L * 1024;
    public long MaxPhotoSize => MaxPhotoSizeMb * 1024L * 1024;
}
