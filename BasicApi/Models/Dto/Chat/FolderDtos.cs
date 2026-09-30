namespace BasicApi.Models.Dto.Chat;

/// <summary>A folder of chats.</summary>
public class FolderDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;

    /// <summary>All private chats, outside the archive.</summary>
    public bool IncludePrivate { get; set; }

    /// <summary>All groups, outside the archive.</summary>
    public bool IncludeGroups { get; set; }

    /// <summary>Only chats with something unread (the pinned ones always).</summary>
    public bool OnlyUnread { get; set; }

    /// <summary>Chats listed explicitly (the pinned ones among them), archived ones too.</summary>
    public List<Guid> ChatIds { get; set; } = [];

    /// <summary>Chats pinned inside the folder, top first.</summary>
    public List<Guid> PinnedChatIds { get; set; } = [];
}

/// <summary><c>FoldersChanged</c>: all the user's folders, in order — to the user's devices.</summary>
public class FoldersDto
{
    public List<FolderDto> Folders { get; set; } = [];
}
