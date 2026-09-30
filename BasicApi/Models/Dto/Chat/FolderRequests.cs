namespace BasicApi.Models.Dto.Chat;

/// <summary>A new folder, or changes to one: a null field stays as it is.</summary>
public class SaveFolderDto
{
    public string? Title { get; set; }
    public bool? IncludePrivate { get; set; }
    public bool? IncludeGroups { get; set; }
    public bool? OnlyUnread { get; set; }
    public List<Guid>? ChatIds { get; set; }
    public List<Guid>? PinnedChatIds { get; set; }
}

public class FolderOrderDto
{
    public List<Guid> FolderIds { get; set; } = [];
}
