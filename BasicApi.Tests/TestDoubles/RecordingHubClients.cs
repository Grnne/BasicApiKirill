using BasicApi.Hubs;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace BasicApi.Tests.TestDoubles;

/// <summary>Одна отправка через SignalR: кому (вид адресата и id) и что.</summary>
public sealed record SentEvent(string Target, IReadOnlyList<string> Ids, string Method, object?[] Args);

/// <summary>
/// Клиенты хаба, которые запоминают отправки вместе с адресатом.
/// SendAsync — метод расширения, Moq его не проверит; здесь видно и кому, и что.
/// </summary>
public sealed class RecordingHubClients : IHubClients
{
    public List<SentEvent> Sent { get; } = [];

    public IEnumerable<SentEvent> Of(string method) => Sent.Where(e => e.Method == method);

    private Proxy To(string target, params string[] ids) => new(this, target, ids);

    public IClientProxy All => To("all");
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => To("allExcept", [.. excludedConnectionIds]);
    public ISingleClientProxy Client(string connectionId) => To("client", connectionId);
    IClientProxy IHubClients<IClientProxy>.Client(string connectionId) => Client(connectionId);
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => To("clients", [.. connectionIds]);
    public IClientProxy Group(string groupName) => To("group", groupName);
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => To("group", groupName);
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => To("groups", [.. groupNames]);
    public IClientProxy User(string userId) => To("user", userId);
    public IClientProxy Users(IReadOnlyList<string> userIds) => To("users", [.. userIds]);

    private sealed class Proxy(RecordingHubClients owner, string target, string[] ids) : ISingleClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            lock (owner.Sent)
                owner.Sent.Add(new SentEvent(target, ids, method, args));
            return Task.CompletedTask;
        }

        public Task<T> InvokeCoreAsync<T>(string method, object?[] args, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}

public sealed class RecordingHubContext : IHubContext<ChatHub>
{
    public RecordingHubClients Recorder { get; } = new();
    public IHubClients Clients => Recorder;
    public IGroupManager Groups { get; } = Mock.Of<IGroupManager>();
}
