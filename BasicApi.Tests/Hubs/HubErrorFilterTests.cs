using BasicApi.Hubs;
using BasicApi.Middleware.Exceptions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Moq;

namespace BasicApi.Tests.Hubs;

public class HubErrorFilterTests
{
    private readonly FakeLogger<HubErrorFilter> _logger = new();
    private readonly HubErrorFilter _filter;
    private readonly HubInvocationContext _invocation;
    private readonly CancellationTokenSource _aborted = new();

    public HubErrorFilterTests()
    {
        _filter = new HubErrorFilter(_logger);

        var context = new Mock<HubCallerContext>();
        context.Setup(c => c.ConnectionId).Returns("conn-1");
        context.Setup(c => c.ConnectionAborted).Returns(_aborted.Token);
        var method = typeof(ChatHub).GetMethod(nameof(ChatHub.SendMessage))!;
        _invocation = new HubInvocationContext(context.Object, Mock.Of<IServiceProvider>(), Mock.Of<Hub>(), method, []);
    }

    private Task Invoke(Exception ex) =>
        _filter.InvokeMethodAsync(_invocation, _ => ValueTask.FromException<object?>(ex)).AsTask();

    [Fact]
    public async Task DomainError_BecomesHubException_WithTheSameCodeAsRest()
    {
        var ex = await Assert.ThrowsAsync<HubException>(() =>
            Invoke(new ForbiddenException("User is not a member of this chat", "NOT_A_MEMBER")));

        Assert.Equal("NOT_A_MEMBER: User is not a member of this chat", ex.Message);
        Assert.Empty(_logger.Collector.GetSnapshot()); // an expected client error, not a server one
    }

    [Fact]
    public async Task HubException_PassesThroughUnchanged()
    {
        var original = HubErrors.Create(HubErrors.RateLimited, "Too many calls. Slow down.");

        var ex = await Assert.ThrowsAsync<HubException>(() => Invoke(original));

        Assert.Same(original, ex);
        Assert.Empty(_logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task UnexpectedError_IsLoggedAsError_AndRethrown()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Invoke(new InvalidOperationException("boom")));

        var record = Assert.Single(_logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Error, record.Level);
        Assert.Contains("SendMessage", record.Message);
    }

    [Fact]
    public async Task CancellationAfterClientLeft_IsNotLogged()
    {
        _aborted.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => Invoke(new OperationCanceledException()));

        Assert.Empty(_logger.Collector.GetSnapshot());
    }
}
