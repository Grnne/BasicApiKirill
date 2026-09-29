namespace BasicApi.Models.Dto.Message;

public class SendMessageDto
{
    /// <summary>Text; leading and trailing whitespace is trimmed, then it must be 1 to 4096 characters.</summary>
    public string? Text { get; set; }

    /// <summary>
    /// Optional id that the client picks itself (a new Guid for each message)
    /// and repeats on retries: a repeat does not create a second message but returns the first.
    /// </summary>
    public Guid? ClientMessageId { get; set; }

    /// <summary>The message being answered; it must be in the same chat and not deleted.</summary>
    public Guid? ReplyToMessageId { get; set; }

    /// <summary>Formatting and mentions; offsets are over <see cref="Text"/> as sent, before trimming.</summary>
    public List<MessageEntityDto>? Entities { get; set; }
}
