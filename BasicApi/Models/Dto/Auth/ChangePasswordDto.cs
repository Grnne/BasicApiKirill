using System.ComponentModel.DataAnnotations;

namespace BasicApi.Models.Dto.Auth;

public class ChangePasswordDto
{
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;

    /// <summary>At least 6 characters and at most 72 bytes in UTF-8, as at registration.</summary>
    [Required]
    [MinLength(6)]
    public string NewPassword { get; set; } = string.Empty;
}
