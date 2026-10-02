using BasicApi.Features.Media;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Moq;

namespace BasicApi.Tests.Features.Media;

public class MediaCleanupTests
{
    [Fact]
    public async Task StorageDown_DuringTheSweep_StopsIt_AndNamesTheObjectsLeftBehind()
    {
        // Found by the review: the rows of unused files go first (their deletion is what checks
        // nothing points to them), then the objects one by one. The storage failing midway left
        // objects no row knew of — silently, and the sweep went on making more of them.
        var attachments = new Mock<IAttachmentRepository>();
        var unused = new Attachment { Id = Guid.NewGuid(), StorageKey = "o/unused", ThumbnailKey = "t/unused" };
        attachments.Setup(a => a.GetStalePendingAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        attachments.Setup(a => a.DeleteUnreferencedAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Repeat(unused, MediaCleanup.BatchSize).ToList());
        var storage = new Mock<IObjectStorage>();
        storage.Setup(s => s.ListOlderAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        storage.Setup(s => s.DeleteAsync(It.Is<IEnumerable<string>>(k => k.Any()), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("storage is down"));
        var logger = new FakeLogger<MediaCleanup>();
        var services = new ServiceCollection()
            .AddScoped(_ => attachments.Object)
            .AddScoped(_ => storage.Object)
            .BuildServiceProvider();
        var cleanup = new MediaCleanup(services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new MediaOptions()), Options.Create(new StorageOptions()), TimeProvider.System, logger);

        await cleanup.CleanupAsync();

        attachments.Verify(a => a.DeleteUnreferencedAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        var record = Assert.Single(logger.Collector.GetSnapshot(), r => r.Level == LogLevel.Error);
        Assert.Contains("o/unused", record.Message);
    }
}
