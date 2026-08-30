namespace BasicApi.Storage.Entities;

/// <summary>
/// One refresh-token session. Rotation creates a new row and marks the previous
/// one as replaced; every row created from a single login shares <see cref="FamilyId"/>.
/// </summary>
public class Session
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }

    /// <summary>Ties together all rotations originating from one login.</summary>
    public Guid FamilyId { get; set; }

    /// <summary>SHA-256 of the refresh token. The token itself is never stored.</summary>
    public string RefreshTokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }

    /// <summary>Set when the session is rotated away or explicitly revoked.</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>Set on rotation — distinguishes "rotated" from "revoked by logout".</summary>
    public Guid? ReplacedBySessionId { get; set; }

    public string? UserAgent { get; set; }
    public string? Ip { get; set; }
}
