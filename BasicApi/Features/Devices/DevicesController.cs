using BasicApi.Extensions;
using BasicApi.Models.Dto.Devices;
using BasicApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BasicApi.Features.Devices;

[Authorize]
[ApiController]
[Route("api/devices")]
[Produces("application/json")]
[Tags("Devices")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
public class DevicesController(IDeviceService devices) : ControllerBase
{
    /// <summary>
    /// The caller's devices: every sign-in that is still open.
    /// </summary>
    /// <remarks>
    /// A device is one sign-in (login or registration) with the token refreshes after it; it is gone
    /// after logout, when its refresh token expires or when it is signed out from another device.
    /// <c>isCurrent</c> marks the one the request came from.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(DeviceListDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDevices(CancellationToken ct)
        => Ok(await devices.GetAllAsync(User.GetUserId(), User.GetSessionFamilyId(), ct));

    /// <summary>
    /// Sign a device out.
    /// </summary>
    /// <remarks>
    /// Its refresh token stops working and its hub connections close at once; an access token it
    /// already holds works until it expires (minutes). The current device may be signed out too —
    /// the same as logout.
    ///
    /// Errors: <c>404 DEVICE_NOT_FOUND</c> — not the caller's or already signed out.
    /// </remarks>
    /// <param name="deviceId">The device id from the list.</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpDelete("{deviceId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SignOutDevice(Guid deviceId, CancellationToken ct)
    {
        await devices.SignOutAsync(User.GetUserId(), deviceId, ct);
        return NoContent();
    }
}
