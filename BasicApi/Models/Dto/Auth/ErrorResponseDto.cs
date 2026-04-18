namespace BasicApi.Models.Dto.Auth;


public class ErrorResponseDto
{
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
}