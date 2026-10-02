using BasicApi.Services;
using BasicApi.Storage.Interfaces;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace BasicApi.Tests.Services;

public class SessionLivenessTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly Mock<ISessionRepository> _sessions = new();
    private readonly Guid _family = Guid.NewGuid();

    public SessionLivenessTests() =>
        _sessions.Setup(s => s.HasLiveSessionInFamilyAsync(_family, It.IsAny<CancellationToken>())).ReturnsAsync(true);

    [Fact]
    public async Task Answer_IsKeptAWhile_ThenAskedAgain()
    {
        var liveness = new SessionLiveness(_time);

        await liveness.IsLiveAsync(_family, _sessions.Object, default);
        await liveness.IsLiveAsync(_family, _sessions.Object, default);
        _time.Advance(SessionLiveness.Keep);
        await liveness.IsLiveAsync(_family, _sessions.Object, default);

        _sessions.Verify(s => s.HasLiveSessionInFamilyAsync(_family, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task SignOut_IsSeenAtOnce()
    {
        var liveness = new SessionLiveness(_time);
        await liveness.IsLiveAsync(_family, _sessions.Object, default);

        _sessions.Setup(s => s.HasLiveSessionInFamilyAsync(_family, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        liveness.Forget(_family);

        Assert.False(await liveness.IsLiveAsync(_family, _sessions.Object, default));
    }

    [Fact]
    public async Task AnswerReadBeforeASignOut_IsNotKeptAfterIt()
    {
        var liveness = new SessionLiveness(_time);
        var reading = new TaskCompletionSource<bool>();
        _sessions.Setup(s => s.HasLiveSessionInFamilyAsync(_family, It.IsAny<CancellationToken>())).Returns(reading.Task);

        var stale = liveness.IsLiveAsync(_family, _sessions.Object, default);
        liveness.ForgetAll();
        reading.SetResult(true);
        Assert.True(await stale);

        _sessions.Setup(s => s.HasLiveSessionInFamilyAsync(_family, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        Assert.False(await liveness.IsLiveAsync(_family, _sessions.Object, default));
    }
}
