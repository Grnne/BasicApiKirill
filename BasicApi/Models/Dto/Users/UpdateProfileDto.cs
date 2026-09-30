namespace BasicApi.Models.Dto.Users;

public class UpdateProfileDto
{
    /// <summary>The name shown to others: 1–100 characters after trimming; null — unchanged.</summary>
    public string? DisplayName { get; set; }
}
