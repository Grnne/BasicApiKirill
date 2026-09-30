namespace BasicApi.Models.Dto.Message;

public class ForwardMessagesDto
{
    /// <summary>The chat the messages are taken from; the caller must be able to read it.</summary>
    public Guid FromChatId { get; set; }

    /// <summary>1 to 100 distinct messages of that chat; they arrive in their original order.</summary>
    public List<Guid> MessageIds { get; set; } = [];

    /// <summary>
    /// Optional, one per message in the same order: a retry with the same ids creates nothing new
    /// and returns the copies made before.
    /// </summary>
    public List<Guid>? ClientMessageIds { get; set; }
}

public class ForwardMessagesResponseDto
{
    /// <summary>The new messages in the target chat, in order.</summary>
    public List<MessageDto> Items { get; set; } = [];
}
