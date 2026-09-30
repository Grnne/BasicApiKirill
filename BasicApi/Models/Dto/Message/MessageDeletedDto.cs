namespace BasicApi.Models.Dto.Message;

/// <summary>Payload of the <c>MessageDeleted</c> event.</summary>
public class MessageDeletedDto
{
    public Guid ChatId { get; set; }
    public Guid MessageId { get; set; }
    public long Seq { get; set; }

    /// <summary>
    /// true — deleted for everyone, all members get the event; false — the user deleted it
    /// for themselves, only their own devices get the event.
    /// </summary>
    public bool ForEveryone { get; set; }
}
