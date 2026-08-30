using System.ComponentModel.DataAnnotations;

namespace BasicApi.Models.Dto.Auth;

public class RefreshTokenRequestDto
{
    [Required(ErrorMessage = "Refresh token is required")]
    public string RefreshToken { get; set; } = string.Empty;
}
