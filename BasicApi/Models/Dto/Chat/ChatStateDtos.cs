namespace BasicApi.Models.Dto.Chat;

public class PinnedChatsDto
{
    /// <summary>The pinned chats, top first. Also the payload of <c>PinnedChatsChanged</c>.</summary>
    public List<Guid> ChatIds { get; set; } = [];
}

/// <summary>How the user keeps a chat; also the payload of <c>ChatStateChanged</c> to their devices.</summary>
public class ChatStateDto
{
    public Guid ChatId { get; set; }
    public bool Archived { get; set; }
    public bool IsMuted { get; set; }

    /// <summary>Muted until then; null — not muted, or for good.</summary>
    public DateTime? MutedUntil { get; set; }
}
