using System.Net;
using BasicApi.Storage.Interfaces;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Options;

namespace BasicApi.Services.Push;

public enum PushDelivery
{
    Sent,

    /// <summary>The push service no longer knows the subscription: remove it.</summary>
    Gone,

    /// <summary>Not delivered this time (the service is down, limits, a timeout).</summary>
    Failed
}

/// <summary>Delivers one notification to one subscription.</summary>
public interface IPushTransport
{
    Task<PushDelivery> SendAsync(DevicePush target, string payload, string topic, CancellationToken ct = default);
}

/// <summary>
/// WebPush: the payload is encrypted for the browser (RFC 8291, <c>aes128gcm</c>), the request is
/// signed with the server's VAPID key (RFC 8292). The push service sees neither the text nor who
/// it is for beyond the endpoint.
/// </summary>
public sealed class WebPushTransport : IPushTransport, IDisposable
{
    private readonly PushServiceClient _client;
    private readonly VapidAuthentication _vapid;
    private readonly int _ttlSeconds;
    private readonly ILogger<WebPushTransport> _logger;

    public WebPushTransport(HttpClient http, IOptions<PushOptions> options, ILogger<WebPushTransport> logger)
    {
        var settings = options.Value;
        // Retry-After is honored by not trying again this time, not by holding the sender up.
        _client = new PushServiceClient(http) { AutoRetryAfter = false };
        _vapid = new VapidAuthentication(settings.VapidPublicKey!, settings.VapidPrivateKey!) { Subject = settings.Subject };
        _ttlSeconds = settings.TtlSeconds;
        _logger = logger;
    }

    public async Task<PushDelivery> SendAsync(DevicePush target, string payload, string topic, CancellationToken ct = default)
    {
        var subscription = new PushSubscription { Endpoint = target.Endpoint };
        subscription.SetKey(PushEncryptionKeyName.P256DH, target.P256dh);
        subscription.SetKey(PushEncryptionKeyName.Auth, target.Auth);
        var message = new PushMessage(payload)
        {
            Topic = topic,
            TimeToLive = _ttlSeconds,
            Urgency = PushMessageUrgency.High
        };

        // The endpoint is a secret of the subscription: only its host goes to the log.
        var host = new Uri(target.Endpoint).Host;
        try
        {
            await _client.RequestPushMessageDeliveryAsync(subscription, message, _vapid, VapidAuthenticationScheme.Vapid, ct);
            return PushDelivery.Sent;
        }
        catch (PushServiceClientException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return PushDelivery.Gone;
        }
        catch (PushServiceClientException ex)
        {
            _logger.LogWarning("Push service {Host} refused a notification: {Status} {Body}", host, (int)ex.StatusCode, ex.Body);
            return PushDelivery.Failed;
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            _logger.LogWarning(ex, "Push service {Host} is unreachable", host);
            return PushDelivery.Failed;
        }
    }

    public void Dispose() => _vapid.Dispose();
}
