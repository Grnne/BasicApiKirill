using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace BasicApi.IntegrationTests.Infrastructure;

/// <summary>
/// One SeaweedFS for the whole run — the same S3 storage as in the compose files. Tests talk to it
/// as clients do: by the signed links the API hands out.
/// </summary>
public sealed class StorageFixture : IAsyncLifetime
{
    // The same version as in docker-compose.prod.yml.
    public const string Image = "chrislusf/seaweedfs:4.47";
    public const string AccessKey = "test-media";
    public const string SecretKey = "test-media-secret-0123456789";
    public const int Port = 8333;

    private readonly IContainer _container = new ContainerBuilder(Image)
        .WithEnvironment("AWS_ACCESS_KEY_ID", AccessKey)
        .WithEnvironment("AWS_SECRET_ACCESS_KEY", SecretKey)
        .WithCommand("mini", "-dir=/data", "-webdav=false", "-admin.ui=false",
            "-s3.port.iceberg=0", "-s3.port.lance=0", "-master.telemetry=false")
        .WithPortBinding(Port, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilHttpRequestIsSucceeded(r => r.ForPort(Port).ForPath("/healthz")))
        .Build();

    public string Endpoint => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(Port)}";

    /// <summary>The application's settings for this storage; the bucket is created by the API itself.</summary>
    public Dictionary<string, string?> Settings(string bucket = "media") => new()
    {
        ["Storage:Endpoint"] = Endpoint,
        ["Storage:Bucket"] = bucket,
        ["Storage:AccessKey"] = AccessKey,
        ["Storage:SecretKey"] = SecretKey,
        ["RateLimiting:CommandsPer10Seconds"] = "1000"
    };

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
