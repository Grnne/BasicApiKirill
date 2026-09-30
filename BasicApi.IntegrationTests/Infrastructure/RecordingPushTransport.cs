using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using BasicApi.Features.Push;
using BasicApi.Storage.Interfaces;

namespace BasicApi.IntegrationTests.Infrastructure;

/// <summary>A push service in memory: records what the server would deliver, in order.</summary>
public sealed class RecordingPushTransport : IPushTransport
{
    public sealed record Delivery(string Endpoint, JsonElement Payload, string Topic);

    private readonly Channel<Delivery> _deliveries = Channel.CreateUnbounded<Delivery>();

    /// <summary>Endpoints the service answers 410 Gone for.</summary>
    public ConcurrentDictionary<string, bool> Gone { get; } = new();

    public Task<PushDelivery> SendAsync(DevicePush target, string payload, string topic, CancellationToken ct = default)
    {
        if (Gone.ContainsKey(target.Endpoint))
            return Task.FromResult(PushDelivery.Gone);
        _deliveries.Writer.TryWrite(new Delivery(target.Endpoint, JsonDocument.Parse(payload).RootElement.Clone(), topic));
        return Task.FromResult(PushDelivery.Sent);
    }

    /// <summary>The next delivery; fails if none comes in time.</summary>
    public async Task<Delivery> NextAsync(TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        return await _deliveries.Reader.ReadAsync(cts.Token);
    }

    /// <summary>Deliveries so far, without waiting.</summary>
    public List<Delivery> Drain()
    {
        var all = new List<Delivery>();
        while (_deliveries.Reader.TryRead(out var delivery)) all.Add(delivery);
        return all;
    }
}
