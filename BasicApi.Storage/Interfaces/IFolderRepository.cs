namespace BasicApi.Storage.Interfaces;

public interface IFolderRepository
{
    /// <summary>The user's folders in their order, with their chats.</summary>
    Task<IReadOnlyList<Folder>> GetAllAsync(Guid userId, CancellationToken ct = default);

    Task<Folder?> GetAsync(Guid userId, Guid folderId, CancellationToken ct = default);

    /// <summary>Inserts or updates the folder with its chats (the listed ones and those pinned inside).</summary>
    Task SaveAsync(Folder folder, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid userId, Guid folderId, CancellationToken ct = default);

    /// <summary>Sets the folders' order: 1, 2, … in the given sequence.</summary>
    Task SetOrderAsync(Guid userId, IReadOnlyList<Guid> folderIds, CancellationToken ct = default);

    /// <summary>Of the given chats, those the user is a member of.</summary>
    Task<IReadOnlyList<Guid>> GetOwnChatsAsync(Guid userId, IReadOnlyCollection<Guid> chatIds, CancellationToken ct = default);
}

public sealed class Folder
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Position { get; set; }
    public bool IncludePrivate { get; set; }
    public bool IncludeGroups { get; set; }
    public bool OnlyUnread { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Chats listed in the folder explicitly (pinned ones included).</summary>
    public List<Guid> ChatIds { get; set; } = [];

    /// <summary>Chats pinned inside the folder, top first.</summary>
    public List<Guid> PinnedChatIds { get; set; } = [];
}
