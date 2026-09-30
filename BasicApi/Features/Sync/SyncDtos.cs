using System.Text.Json;
using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Users;

namespace BasicApi.Features.Sync;

/// <summary>Snapshot: the user's state to start from, the same on every device; chat history comes page by page.</summary>
public class SyncStateDto
{
    /// <summary>Number of the last change included in the snapshot. From it — <c>GET /api/sync?since=</c>.</summary>
    public long Pts { get; set; }

    /// <summary>
    /// All the user's chats, archived included — like <c>GET /api/chats</c>: counters, pins,
    /// archive, mute, drafts.
    /// </summary>
    public List<ChatListItemDto> Chats { get; set; } = [];

    /// <summary>The user's folders, in order — like <c>GET /api/folders</c>.</summary>
    public List<FolderDto> Folders { get; set; } = [];

    /// <summary>Privacy settings — like <c>GET /api/users/me/privacy</c>.</summary>
    public PrivacySettingsDto Privacy { get; set; } = new();

    /// <summary>Whom the user has blocked; profiles — <c>GET /api/users/me/blocked</c>.</summary>
    public List<Guid> BlockedUserIds { get; set; } = [];

    /// <summary>The user's own profile — like <c>GET /api/users/me</c>.</summary>
    public OwnProfileResponseDto Me { get; set; } = new();
}

/// <summary>Changes after the pts known to the client.</summary>
public class SyncDifferenceDto
{
    /// <summary>Changes in pts order.</summary>
    public List<SyncUpdateDto> Updates { get; set; } = [];

    /// <summary>The pts up to which the client is now up to date: pass it to the next request as <c>since</c>.</summary>
    public long Pts { get; set; }

    /// <summary>There are more changes — repeat the request with the new <c>since</c>.</summary>
    public bool HasMore { get; set; }

    /// <summary>
    /// No diff available: part of the changes has already been removed from the journal (the client did not
    /// sign in within the retention period) or <c>since</c> is not from this journal. Take a new snapshot via
    /// <c>GET /api/sync/state</c>.
    /// </summary>
    public bool SnapshotRequired { get; set; }
}

public class SyncUpdateDto
{
    public long Pts { get; set; }

    /// <summary>Hub event name: <c>MessageCreated</c>, <c>MessageUpdated</c>, <c>MessageDeleted</c>, <c>ReactionsChanged</c>, <c>MessagesDelivered</c>, <c>MessagesRead</c>, <c>ReadStateChanged</c>, <c>DraftUpdated</c>, <c>ChatCreated</c>, <c>ChatUpdated</c>,
    /// <c>ChatDeleted</c>, <c>MemberAdded</c>, <c>MemberRemoved</c>, <c>MemberUpdated</c>, <c>UserUpdated</c>, <c>PrivacyUpdated</c>,
    /// <c>BlockListChanged</c>, <c>PinnedChatsChanged</c>, <c>ChatStateChanged</c>,
    /// <c>FoldersChanged</c>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The same object that arrives in the hub event with this name.</summary>
    public JsonElement Payload { get; set; }

    public DateTime CreatedAt { get; set; }
}

public class SyncAckDto
{
    /// <summary>pts of the last change the device received and processed.</summary>
    public long Pts { get; set; }
}
