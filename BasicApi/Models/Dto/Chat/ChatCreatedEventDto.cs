namespace BasicApi.Models.Dto.Chat;

/// <summary>
/// SignalR event payload for ChatCreated.
/// Sent to chat participants when a new chat is created,
/// so they can add it to their chat list without refreshing.
/// </summary>
public class ChatCreatedEventDto
{
    /// <summary>Chat type: "private" or "group".</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Title for group chats, null for private.</summary>
    public string? Title { get; set; }

    /// <summary>Companion display name (for private chats).</summary>
    public string? CompanionName { get; set; }

    /// <summary>Companion username (for private chats).</summary>
    public string? CompanionUsername { get; set; }
}
