namespace BasicApi.Models.Dto.Chat;

public class SetPinnedDto
{
    public bool Pinned { get; set; }
}

public class PinnedChatsDto
{
    /// <summary>The pinned chats, top first. Also the payload of <c>PinnedChatsChanged</c>.</summary>
    public List<Guid> ChatIds { get; set; } = [];
}

public class SetArchivedDto
{
    public bool Archived { get; set; }
}

public class SetMutedDto
{
    /// <summary>false — unmute.</summary>
    public bool Muted { get; set; }

    /// <summary>Until when; null with <see cref="Muted"/> — for good.</summary>
    public DateTime? Until { get; set; }
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
