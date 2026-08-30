namespace BasicApi.Models.Dto.Auth;

public class AuthResponseDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Short-lived JWT for API and hub calls. Send as <c>Authorization: Bearer</c>.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>When <see cref="Token"/> expires (UTC).</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Long-lived opaque token used to obtain a new access token via
    /// <c>POST /api/auth/refresh</c>. Rotated on every refresh — always store the
    /// latest value and never send it anywhere except the refresh endpoint.
    /// </summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>When <see cref="RefreshToken"/> expires (UTC). After that the user must log in again.</summary>
    public DateTime RefreshTokenExpiresAt { get; set; }
}
