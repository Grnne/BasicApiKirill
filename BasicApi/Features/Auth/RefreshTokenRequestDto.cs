using System.ComponentModel.DataAnnotations;

namespace BasicApi.Features.Auth;

public class RefreshTokenRequestDto
{
    [Required(ErrorMessage = "Refresh token is required")]
    public string RefreshToken { get; set; } = string.Empty;
}
