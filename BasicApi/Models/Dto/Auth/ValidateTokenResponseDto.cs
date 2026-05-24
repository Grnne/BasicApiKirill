namespace BasicApi.Models.Dto.Auth;

public class ValidateTokenResponseDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public bool IsValid { get; set; } = true;
}
