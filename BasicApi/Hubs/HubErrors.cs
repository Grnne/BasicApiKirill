using Microsoft.AspNetCore.SignalR;

namespace BasicApi.Hubs;

/// <summary>
/// Hub method errors. Only the text of <see cref="HubException"/> reaches the client,
/// so the error code goes at the start of the message: <c>"NOT_A_MEMBER: ..."</c>. The codes are the same
/// as in REST: domain errors from the services are translated by <see cref="HubErrorFilter"/>.
/// SignalR adds its own prefix on the wire, and the client receives
/// <c>"An unexpected error occurred invoking 'JoinChat' on the server. HubException: NOT_A_MEMBER: ..."</c>.
/// The code is the word right after <c>"HubException: "</c> (verified by an E2E test over WebSocket).
/// </summary>
public static class HubErrors
{
    public const string NotAMember = "NOT_A_MEMBER";
    public const string RateLimited = "RATE_LIMITED";

    public static HubException Create(string code, string message) => new($"{code}: {message}");
}
