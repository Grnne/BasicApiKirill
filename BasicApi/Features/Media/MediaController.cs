using BasicApi.Extensions;
using BasicApi.Models.Dto.Message;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BasicApi.Features.Media;

[Authorize]
[ApiController]
[Route("api/media")]
[Produces("application/json")]
[Tags("Media")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public class MediaController(IMediaService media) : ControllerBase
{
    /// <summary>
    /// Start an upload.
    /// </summary>
    /// <remarks>
    /// Three steps, and the file never passes through the API:
    /// 1. this call — the server checks the kind and the declared size and answers with a signed link;
    /// 2. <c>PUT {uploadUrl}</c> with the file as the body and exactly <c>Content-Type: {contentType}</c>;
    ///    for a video with <c>withThumbnail</c> — also a frame (JPEG or PNG) to <c>thumbnailUploadUrl</c>;
    /// 3. <c>POST /api/media/uploads/{attachmentId}/complete</c>.
    ///
    /// Limits: <c>Media:MaxFileSizeMb</c> (100 MB), photos <c>Media:MaxPhotoSizeMb</c> (20 MB),
    /// <c>Media:MaxPendingUploads</c> unfinished uploads per user. Voice needs <c>durationMs</c>;
    /// video — <c>width</c> and <c>height</c> if known.
    ///
    /// Errors: <c>400 INVALID_MEDIA</c>, <c>400 FILE_TOO_LARGE</c>, <c>409 TOO_MANY_UPLOADS</c>,
    /// <c>503 MEDIA_UNAVAILABLE</c> — the server has no file storage.
    /// </remarks>
    [HttpPost("uploads")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(typeof(UploadTicketDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateUpload([FromBody] CreateUploadDto dto, CancellationToken ct)
        => Created(string.Empty, await media.CreateUploadAsync(User.GetUserId(), dto, ct));

    /// <summary>
    /// Finish an upload: the server checks the file and makes it usable.
    /// </summary>
    /// <remarks>
    /// The server reads what was uploaded: the real size, the SHA-256, the type by content. A photo
    /// must be a JPEG, PNG, GIF or WebP picture — the server measures it and makes a preview; a video
    /// must be MP4, MOV or WebM, a voice message Ogg, WebM, MP4 or MP3. A file that fails the check is
    /// removed. Repeating the call returns the same file.
    ///
    /// Errors: <c>400 UPLOAD_INCOMPLETE</c> — nothing was put at the link yet,
    /// <c>400 FILE_TOO_LARGE</c>, <c>400 INVALID_MEDIA</c>, <c>404 UPLOAD_NOT_FOUND</c> — no such
    /// upload of the caller (or it went stale and was removed).
    /// </remarks>
    [HttpPost("uploads/{attachmentId:guid}/complete")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(typeof(AttachmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CompleteUpload(Guid attachmentId, CancellationToken ct)
        => Ok(await media.CompleteUploadAsync(User.GetUserId(), attachmentId, ct));

    /// <summary>
    /// Download links for files.
    /// </summary>
    /// <remarks>
    /// Up to 100 ids at once — a screen of previews in one request. Files the caller may not see are
    /// left out of the answer. Links are signed and work for <c>Media:DownloadUrlMinutes</c> (60)
    /// minutes. Photos, videos and voice are served for display; other files only as a download of
    /// unknown type.
    ///
    /// Errors: <c>400 INVALID_REQUEST</c>.
    /// </remarks>
    [HttpPost("links")]
    [ProducesResponseType(typeof(MediaLinksDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetLinks([FromBody] MediaLinksRequestDto dto, CancellationToken ct)
        => Ok(await media.GetLinksAsync(User.GetUserId(), dto.AttachmentIds, ct));

    /// <summary>
    /// Redirect to a file (or its preview).
    /// </summary>
    /// <remarks>
    /// <c>302</c> to a signed link — for code that downloads with the access token. For
    /// <c>&lt;img&gt;</c> use links from <c>POST /api/media/links</c>: a browser does not send the
    /// token with a picture request.
    ///
    /// Errors: <c>404 ATTACHMENT_NOT_FOUND</c> — no such file, the caller may not see it, or (for the
    /// file itself) it has expired.
    /// </remarks>
    /// <param name="attachmentId">The file.</param>
    /// <param name="thumbnail">true — the preview.</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpGet("{attachmentId:guid}")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(Guid attachmentId, [FromQuery] bool thumbnail = false, CancellationToken ct = default)
    {
        var link = (await media.GetLinksAsync(User.GetUserId(), [attachmentId], ct)).Items.FirstOrDefault();
        var url = thumbnail ? link?.ThumbnailUrl : link?.Url;
        return url is null
            ? throw new Middleware.Exceptions.NotFoundException("No such file", "ATTACHMENT_NOT_FOUND")
            : Redirect(url);
    }
}
