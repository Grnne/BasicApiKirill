namespace BasicApi.Models.Dto.Message;

/// <summary>
/// <c>MessagesDelivered</c> / <c>MessagesRead</c>: a member's device received, or the member read,
/// the chat up to <see cref="Seq"/>. Goes to the authors whose messages first got that status.
/// </summary>
public class ReceiptDto
{
    public Guid ChatId { get; set; }

    /// <summary>Who received or read.</summary>
    public Guid UserId { get; set; }

    /// <summary>Everything up to this seq, the recipient's own messages included.</summary>
    public long Seq { get; set; }
}
