namespace BasicApi.Models.Dto.Auth;

public class LogoutRequestDto
{
    /// <summary>
    /// Refresh token of the session to end. Optional: when omitted, only the
    /// access token is discarded client-side and the session stays alive until
    /// it expires — so clients should always send it.
    /// </summary>
    public string? RefreshToken { get; set; }
}
