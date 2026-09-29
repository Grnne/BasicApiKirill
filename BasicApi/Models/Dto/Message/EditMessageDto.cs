namespace BasicApi.Models.Dto.Message;

public class EditMessageDto
{
    /// <summary>New text; the same rules as when sending.</summary>
    public string? Text { get; set; }

    /// <summary>The new formatting, replacing the old one; none — plain text.</summary>
    public List<MessageEntityDto>? Entities { get; set; }
}
