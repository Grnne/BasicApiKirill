using BasicApi.Features.Groups;
using BasicApi.Features.Media;
using BasicApi.Features.Push;
using BasicApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BasicApi.Features.Config;

[Authorize]
[ApiController]
[Route("api/config")]
[Produces("application/json")]
[Tags("Config")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
public class ConfigController(
    IOptions<MessageOptions> messages,
    IOptions<StorageOptions> storage,
    IOptions<MediaOptions> media,
    IOptions<GroupOptions> groups,
    IOptions<PushOptions> push) : ControllerBase
{
    /// <summary>
    /// The instance's limits and switches.
    /// </summary>
    /// <remarks>
    /// What a client checks before sending, so a user learns about a limit before the server rejects
    /// the request: message length, files per album, file sizes, the reaction set, group size, and
    /// whether files and push are available. Set by the server's configuration; read once after login.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(ClientConfigDto), StatusCodes.Status200OK)]
    public IActionResult Get() =>
        Ok(ClientConfigDto.From(messages.Value, storage.Value, media.Value, groups.Value, push.Value));
}
