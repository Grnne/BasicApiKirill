using BasicApi.Extensions;
using BasicApi.Models.Dto.Push;
using BasicApi.Services.Push;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BasicApi.Features.Push;

[Authorize]
[ApiController]
[Route("api/push")]
[Produces("application/json")]
[Tags("Push")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
public class PushController(IPushService push) : ControllerBase
{
    /// <summary>
    /// Whether the server sends push notifications, and its key to subscribe with.
    /// </summary>
    /// <remarks>
    /// Pass <c>vapidPublicKey</c> as <c>applicationServerKey</c> to <c>pushManager.subscribe</c>.
    /// </remarks>
    [HttpGet("config")]
    [ProducesResponseType(typeof(PushConfigDto), StatusCodes.Status200OK)]
    public IActionResult GetConfig() => Ok(push.GetConfig());

    /// <summary>
    /// Subscribe this device to push notifications.
    /// </summary>
    /// <remarks>
    /// The body is the browser's <c>PushSubscription.toJSON()</c>. One subscription per device (the
    /// sign-in the token belongs to): a new one replaces the old. A subscription the same browser
    /// gave another sign-in — for instance, before logging in as someone else — moves to this one.
    /// Logout and signing the device out remove it. Only push services the server knows are taken:
    /// Google (Chrome), Mozilla, Microsoft (Edge) and Apple (Safari) by default.
    ///
    /// Errors: <c>400 INVALID_SUBSCRIPTION</c>, <c>404 DEVICE_NOT_FOUND</c> (the sign-in has already
    /// ended), <c>503 PUSH_UNAVAILABLE</c> (push is off on this server).
    /// </remarks>
    [HttpPut("subscription")]
    [EnableRateLimiting(ServiceExtensions.CommandsRateLimitPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Subscribe([FromBody] PushSubscriptionDto subscription, CancellationToken ct)
    {
        await push.SubscribeAsync(User.GetUserId(), User.GetSessionFamilyId(), subscription, ct);
        return NoContent();
    }

    /// <summary>
    /// Stop push notifications to this device.
    /// </summary>
    /// <remarks>Without a subscription — also <c>204</c>.</remarks>
    [HttpDelete("subscription")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unsubscribe(CancellationToken ct)
    {
        await push.UnsubscribeAsync(User.GetUserId(), User.GetSessionFamilyId(), ct);
        return NoContent();
    }
}
