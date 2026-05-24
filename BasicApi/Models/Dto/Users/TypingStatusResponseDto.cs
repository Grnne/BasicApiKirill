namespace BasicApi.Models.Dto.Users;

public class TypingStatusDto
{
    public Guid UserId { get; set; }
    public Guid ChatId { get; set; }
    public bool IsTyping { get; set; }
}

public class TypingStatusResponseDto
{
    public List<TypingStatusDto> Items { get; set; } = [];
}
