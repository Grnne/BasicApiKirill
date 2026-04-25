namespace BasicApi.Models.Dto.Chat;

/// <summary>
/// Response for chat creation — returns the created or existing chat ID.
/// </summary>
public class CreateChatResponseDto
{
    public Guid ChatId { get; set; }
}
