namespace BasicApi.Features.Chats;

public class SetPinnedDto
{
    public bool Pinned { get; set; }
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
