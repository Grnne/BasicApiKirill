using System.Text.Json;
using BasicApi.Models.Dto.Message;

namespace BasicApi.Services.Events;

/// <summary>
/// What to dispatch for a single outbox event: one or more SignalR sends.
/// The arguments are stored as ready-made JSON — the dispatcher hands them to the hub as is, and the client
/// receives the same as it would with a direct send.
/// </summary>
public sealed record OutboxEnvelope(IReadOnlyList<HubSend> Sends)
{
    /// <summary>The same settings as the SignalR JSON protocol and REST: camelCase.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public static OutboxEnvelope Deserialize(string json) =>
        JsonSerializer.Deserialize<OutboxEnvelope>(json, Json)
        ?? throw new InvalidOperationException("Empty outbox payload");
}

/// <param name="Target">
/// <c>group</c>, <c>user</c> or <c>users</c> — an event; <c>leave-group</c> — take the users'
/// connections out of a hub group; <c>push</c> — push notifications of a new message.
/// </param>
/// <param name="Ids">Group name or user ids; for <c>push</c> — the chat and the sender.</param>
/// <param name="Method">Event name on the client; for <c>leave-group</c> — the group.</param>
/// <param name="Args">Event arguments.</param>
public sealed record HubSend(string Target, IReadOnlyList<string> Ids, string Method, IReadOnlyList<JsonElement> Args)
{
    public static HubSend Group(Guid chatId, string method, params object?[] args) =>
        new("group", [chatId.ToString()], method, ToJson(args));

    public static HubSend User(Guid userId, string method, params object?[] args) =>
        new("user", [userId.ToString()], method, ToJson(args));

    public static HubSend Users(IEnumerable<Guid> userIds, string method, params object?[] args) =>
        new("users", [.. userIds.Select(id => id.ToString())], method, ToJson(args));

    public static HubSend LeaveGroup(IEnumerable<Guid> userIds, Guid chatId) =>
        new("leave-group", [.. userIds.Select(id => id.ToString())], chatId.ToString(), []);

    /// <summary>Recipients are chosen when it is sent: members, their devices and settings then.</summary>
    public static HubSend Push(Guid chatId, Guid senderId, PushNotificationDto notification) =>
        new("push", [chatId.ToString(), senderId.ToString()], "Push", ToJson([notification]));

    private static JsonElement[] ToJson(object?[] args) =>
        [.. args.Select(a => JsonSerializer.SerializeToElement(a, a?.GetType() ?? typeof(object), OutboxEnvelope.Json))];
}
