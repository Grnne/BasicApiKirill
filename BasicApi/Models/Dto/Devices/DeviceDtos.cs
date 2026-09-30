namespace BasicApi.Models.Dto.Devices;

/// <summary>A device of the user: one sign-in that is still open.</summary>
public class DeviceDto
{
    /// <summary>The sign-in id (<c>sid</c> in its access tokens).</summary>
    public Guid Id { get; set; }

    /// <summary>The device the request came from.</summary>
    public bool IsCurrent { get; set; }

    public DateTime SignedInAt { get; set; }

    /// <summary>The last token refresh: an open client does it every few minutes.</summary>
    public DateTime LastActiveAt { get; set; }

    /// <summary>The browser or app, as it introduced itself at the last refresh.</summary>
    public string? UserAgent { get; set; }
}

public class DeviceListDto
{
    /// <summary>The most recently active first.</summary>
    public List<DeviceDto> Items { get; set; } = [];
}
