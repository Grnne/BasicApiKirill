using System.ComponentModel.DataAnnotations;

namespace BasicApi.Features.Auth;

public class RegisterRequestDto
{
    [Required]
    [MinLength(3)]
    [MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(6)]
    public string Password { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? DisplayName { get; set; }

    /// <summary>
    /// A member's one-time invitation (<c>POST /api/auth/invites</c>) — needed when registration is
    /// by invitation (<c>GET /api/auth/registration</c>), ignored otherwise.
    /// </summary>
    [MaxLength(100)]
    public string? InviteCode { get; set; }
}