namespace BasicApi.Features.Groups;

/// <summary>Instance-wide group rules, section <c>Groups</c> of the configuration.</summary>
public sealed class GroupOptions
{
    public const string Section = "Groups";

    /// <summary>How many members a group may have, the owner included.</summary>
    public int MaxMembers { get; set; } = 500;

    public const int MaxTitleLength = 128;
}
