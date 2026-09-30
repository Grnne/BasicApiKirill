using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using BasicApi.Middleware.Exceptions;
using Microsoft.Extensions.Options;

namespace BasicApi.Services.Media;

/// <summary>
/// The file storage as the application needs it. Clients upload and download straight to and
/// from the storage by short-lived signed links; the server reads an object only to check it.
/// </summary>
public interface IObjectStorage
{
    /// <summary>A link to upload one object with exactly this <c>Content-Type</c> header.</summary>
    Task<Uri> PresignPutAsync(string key, string contentType, TimeSpan lifetime);

    /// <summary>A link to download the object; the storage answers with the given headers.</summary>
    Task<Uri> PresignGetAsync(string key, TimeSpan lifetime, string contentType, string contentDisposition);

    /// <summary>The object's size; null when there is no such object.</summary>
    Task<long?> GetSizeAsync(string key, CancellationToken ct = default);

    Task<Stream> OpenReadAsync(string key, CancellationToken ct = default);

    Task PutAsync(string key, Stream content, string contentType, CancellationToken ct = default);

    /// <summary>Removes the objects; missing ones are not an error.</summary>
    Task DeleteAsync(IEnumerable<string> keys, CancellationToken ct = default);
}

/// <summary>
/// Any S3-compatible storage (SeaweedFS in the compose files). Two clients: one talks to the
/// storage over the internal network, the other only signs links for the address clients use —
/// the signature covers the host, so a link must be signed for the host it will be opened at.
/// </summary>
public sealed class S3ObjectStorage : IObjectStorage, IDisposable
{
    private readonly StorageOptions _options;
    private readonly AmazonS3Client? _client;
    private readonly AmazonS3Client? _signer;
    private readonly SemaphoreSlim _bucketLock = new(1, 1);
    private volatile bool _bucketReady;

    public S3ObjectStorage(IOptions<StorageOptions> options)
    {
        _options = options.Value;
        if (!_options.IsConfigured)
            return;

        _client = CreateClient(_options.Endpoint!);
        _signer = string.IsNullOrWhiteSpace(_options.PublicUrl) ? _client : CreateClient(_options.PublicUrl!);
    }

    private AmazonS3Client CreateClient(string serviceUrl) =>
        new(new BasicAWSCredentials(_options.AccessKey, _options.SecretKey), new AmazonS3Config
        {
            ServiceURL = serviceUrl,
            ForcePathStyle = true,
            AuthenticationRegion = _options.Region,
            // Checksums the SDK adds by default are not understood by every S3-compatible server.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
        });

    private AmazonS3Client Client => _client ?? throw Unavailable();
    private AmazonS3Client Signer => _signer ?? throw Unavailable();

    public Task<Uri> PresignPutAsync(string key, string contentType, TimeSpan lifetime) =>
        PresignAsync(new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            ContentType = contentType,
            Expires = DateTime.UtcNow.Add(lifetime)
        });

    public Task<Uri> PresignGetAsync(string key, TimeSpan lifetime, string contentType, string contentDisposition)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(lifetime)
        };
        // The storage answers with our headers, not with whatever the uploader claimed.
        request.ResponseHeaderOverrides.ContentType = contentType;
        request.ResponseHeaderOverrides.ContentDisposition = contentDisposition;
        request.ResponseHeaderOverrides.CacheControl = "private, max-age=3600";
        return PresignAsync(request);
    }

    private async Task<Uri> PresignAsync(GetPreSignedUrlRequest request)
    {
        var signer = Signer;
        request.Protocol = signer.Config.ServiceURL.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? Protocol.HTTP
            : Protocol.HTTPS;
        await EnsureBucketAsync(CancellationToken.None);
        return new Uri(await signer.GetPreSignedURLAsync(request));
    }

    public async Task<long?> GetSizeAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var meta = await Client.GetObjectMetadataAsync(_options.Bucket, key, ct);
            return meta.ContentLength;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var response = await Client.GetObjectAsync(_options.Bucket, key, ct);
        return response.ResponseStream;
    }

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        await EnsureBucketAsync(ct);
        await Client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false
        }, ct);
    }

    public async Task DeleteAsync(IEnumerable<string> keys, CancellationToken ct = default)
    {
        // One by one: the batch call needs checksums not every S3-compatible server takes.
        foreach (var key in keys)
            await Client.DeleteObjectAsync(_options.Bucket, key, ct);
    }

    /// <summary>The bucket is created on first use: the storage may start empty.</summary>
    private async Task EnsureBucketAsync(CancellationToken ct)
    {
        if (_bucketReady)
            return;
        await _bucketLock.WaitAsync(ct);
        try
        {
            if (_bucketReady)
                return;
            try
            {
                await Client.PutBucketAsync(_options.Bucket, ct);
            }
            catch (AmazonS3Exception ex) when (ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
            {
            }
            _bucketReady = true;
        }
        finally
        {
            _bucketLock.Release();
        }
    }

    private static ServiceUnavailableException Unavailable() =>
        new("File storage is not configured on this server", "MEDIA_UNAVAILABLE");

    public void Dispose()
    {
        if (!ReferenceEquals(_signer, _client))
            _signer?.Dispose();
        _client?.Dispose();
        _bucketLock.Dispose();
    }
}
