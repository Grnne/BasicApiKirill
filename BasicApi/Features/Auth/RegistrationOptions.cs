namespace BasicApi.Features.Auth;

/// <summary>Who may create an account, section <c>Registration</c> of the configuration.</summary>
public sealed class RegistrationOptions
{
    public const string Section = "Registration";

    public const string Open = "open";
    public const string Closed = "closed";
    public const string Invite = "invite";

    /// <summary><c>open</c> — anyone; <c>closed</c> — nobody; <c>invite</c> — with a code a member made.</summary>
    public string Mode { get; set; } = Open;

    /// <summary>How long an invitation works.</summary>
    public int InviteDays { get; set; } = 7;

    /// <summary>How many unused invitations one member may hold at once.</summary>
    public int MaxOpenInvites { get; set; } = 20;
}
