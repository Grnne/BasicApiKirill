using BasicApi.Extensions;
using BasicApi.Models.Dto.Sync;
using BasicApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BasicApi.Features.Sync;

[Authorize]
[ApiController]
[Route("api/sync")]
[Produces("application/json")]
[Tags("Sync")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
public class SyncController(ISyncService sync) : ControllerBase
{
    /// <summary>
    /// Snapshot to start from: the chat list and the current <c>pts</c>.
    /// </summary>
    /// <remarks>
    /// Call on first start and whenever <c>GET /api/sync</c> answers
    /// <c>snapshotRequired: true</c>. Keep <c>pts</c>: it is the <c>since</c> of the next
    /// <c>GET /api/sync</c>.
    /// </remarks>
    /// <param name="ct">Request cancellation.</param>
    [HttpGet("state")]
    [ProducesResponseType(typeof(SyncStateDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetState(CancellationToken ct)
        => Ok(await sync.GetStateAsync(User.GetUserId(), ct));

    /// <summary>
    /// Updates after <c>since</c>, in order.
    /// </summary>
    /// <remarks>
    /// After a reconnect: apply <c>updates</c> in order (each has the same payload as the
    /// hub event of the same name), store the returned <c>pts</c>, repeat while
    /// <c>hasMore</c>. An update may also have arrived over the hub — recognize it by
    /// the message or chat id.
    ///
    /// <c>snapshotRequired: true</c> — the journal no longer reaches back to
    /// <c>since</c> (kept for 30 days): take <c>GET /api/sync/state</c> instead.
    /// </remarks>
    /// <param name="since">The last pts the client knows (0 — nothing yet).</param>
    /// <param name="limit">Updates per page (default 100, max 500).</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpGet]
    [ProducesResponseType(typeof(SyncDifferenceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDifference([FromQuery] long since, [FromQuery] int limit = 100, CancellationToken ct = default)
        => Ok(await sync.GetDifferenceAsync(User.GetUserId(), since, Math.Clamp(limit, 1, 500), ct));

    /// <summary>
    /// Confirm that this device has received updates up to <c>pts</c>.
    /// </summary>
    /// <remarks>
    /// The device is the sign-in behind the access token. The acknowledged pts only moves
    /// forward. New messages the journal carried up to this pts become <c>delivered</c>: their
    /// authors get <c>MessagesDelivered</c> if no other member had received them before.
    /// </remarks>
    /// <param name="dto">The last received pts.</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpPost("ack")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Ack([FromBody] SyncAckDto dto, CancellationToken ct)
    {
        // Tokens without sid (issued before binding to a session) expired long ago;
        // just in case, a shared "device".
        await sync.AckAsync(User.GetUserId(), User.GetSessionFamilyId() ?? Guid.Empty, dto.Pts, ct);
        return NoContent();
    }
}
