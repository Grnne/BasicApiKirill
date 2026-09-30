namespace BasicApi.Models.Dto.Chat;

public class MarkUnreadDto
{
    /// <summary>true — mark the chat as unread, false — remove the mark.</summary>
    public bool MarkedUnread { get; set; }
}
