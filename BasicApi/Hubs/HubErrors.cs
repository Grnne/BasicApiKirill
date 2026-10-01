using Microsoft.AspNetCore.SignalR;

namespace BasicApi.Hubs;

/// <summary>
/// Hub method errors: only the <see cref="HubException"/> text reaches the client, so it starts with the REST error code
/// (<c>"NOT_A_MEMBER: ..."</c>). SignalR prefixes it on the wire; the code is the word after <c>"HubException: "</c>.
/// </summary>
public static class HubErrors
{
    public const string NotAMember = "NOT_A_MEMBER";
    public const string RateLimited = "RATE_LIMITED";

    public static HubException Create(string code, string message) => new($"{code}: {message}");
}
