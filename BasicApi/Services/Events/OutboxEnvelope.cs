using System.Text.Json;

namespace BasicApi.Services.Events;

/// <summary>
/// Что разослать по одному событию из outbox: одна или несколько отправок SignalR.
/// Аргументы хранятся готовым JSON — диспетчер отдаёт их хабу как есть, и клиент
/// получает то же, что получил бы при прямой отправке.
/// </summary>
public sealed record OutboxEnvelope(IReadOnlyList<HubSend> Sends)
{
    /// <summary>Те же настройки, что у JSON-протокола SignalR и у REST: camelCase.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public static OutboxEnvelope Deserialize(string json) =>
        JsonSerializer.Deserialize<OutboxEnvelope>(json, Json)
        ?? throw new InvalidOperationException("Empty outbox payload");
}

/// <param name="Target"><c>group</c>, <c>user</c> или <c>users</c>.</param>
/// <param name="Ids">Имя группы или id пользователей.</param>
/// <param name="Method">Имя события на клиенте.</param>
/// <param name="Args">Аргументы события.</param>
public sealed record HubSend(string Target, IReadOnlyList<string> Ids, string Method, IReadOnlyList<JsonElement> Args)
{
    public static HubSend Group(Guid chatId, string method, params object?[] args) =>
        new("group", [chatId.ToString()], method, ToJson(args));

    public static HubSend User(Guid userId, string method, params object?[] args) =>
        new("user", [userId.ToString()], method, ToJson(args));

    public static HubSend Users(IEnumerable<Guid> userIds, string method, params object?[] args) =>
        new("users", [.. userIds.Select(id => id.ToString())], method, ToJson(args));

    private static JsonElement[] ToJson(object?[] args) =>
        [.. args.Select(a => JsonSerializer.SerializeToElement(a, a?.GetType() ?? typeof(object), OutboxEnvelope.Json))];
}
