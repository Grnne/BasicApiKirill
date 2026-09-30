namespace BasicApi.Models.Dto.Message;

public class ReactionCountDto
{
    public string Emoji { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>
/// The reactions of a message after a change: the answer to setting a reaction and the payload
/// of the <c>ReactionsChanged</c> event.
/// </summary>
public class MessageReactionsDto
{
    public Guid ChatId { get; set; }
    public Guid MessageId { get; set; }

    /// <summary>All reactions of the message, most popular first.</summary>
    public List<ReactionCountDto> Reactions { get; set; } = [];

    /// <summary>Who changed their reaction.</summary>
    public Guid UserId { get; set; }

    /// <summary>Their reaction now; null — they removed it.</summary>
    public string? Emoji { get; set; }
}
