using BasicApi.Services;
using Microsoft.Extensions.Time.Testing;

namespace BasicApi.Tests.Services;

public class UserStatusServiceTypingTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly UserStatusService _service;
    private readonly Guid _chat = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();

    public UserStatusServiceTypingTests() => _service = new UserStatusService(_time);

    [Fact]
    public async Task Typing_ExpiresAfterTtl_WhenClientNeverSendsStop()
    {
        // Клиент закрыл вкладку посреди набора — «печатает» не должно висеть вечно.
        await _service.SetTypingAsync(_chat, _user, true);

        _time.Advance(UserStatusService.TypingTtl - TimeSpan.FromSeconds(1));
        Assert.Contains(_user, (await _service.GetTypingStatusAsync([_chat]))[_chat]);

        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Empty(await _service.GetTypingStatusAsync([_chat]));
    }

    [Fact]
    public async Task Typing_Repeated_ExtendsTtl()
    {
        await _service.SetTypingAsync(_chat, _user, true);
        _time.Advance(UserStatusService.TypingTtl - TimeSpan.FromSeconds(1));
        await _service.SetTypingAsync(_chat, _user, true);
        _time.Advance(TimeSpan.FromSeconds(2));

        Assert.Contains(_user, (await _service.GetTypingStatusAsync([_chat]))[_chat]);
    }

    [Fact]
    public async Task Typing_Stop_RemovesImmediately()
    {
        await _service.SetTypingAsync(_chat, _user, true);
        await _service.SetTypingAsync(_chat, _user, false);

        Assert.Empty(await _service.GetTypingStatusAsync([_chat]));
    }

    [Fact]
    public async Task GetTypingStatus_ReturnsOnlyRequestedChats()
    {
        var other = Guid.NewGuid();
        await _service.SetTypingAsync(_chat, _user, true);
        await _service.SetTypingAsync(other, Guid.NewGuid(), true);

        var result = await _service.GetTypingStatusAsync([_chat]);

        Assert.Equal([_chat], result.Keys);
    }

    [Fact]
    public async Task ClearTyping_RemovesUserEverywhere_AndReportsChats()
    {
        var other = Guid.NewGuid();
        var someoneElse = Guid.NewGuid();
        await _service.SetTypingAsync(_chat, _user, true);
        await _service.SetTypingAsync(other, _user, true);
        await _service.SetTypingAsync(other, someoneElse, true);

        var cleared = await _service.ClearTypingAsync(_user);

        Assert.Equal(new[] { _chat, other }.Order(), cleared.Order());
        var left = await _service.GetTypingStatusAsync([_chat, other]);
        Assert.False(left.ContainsKey(_chat));
        Assert.Equal([someoneElse], left[other]);
    }

    [Fact]
    public async Task ClearTyping_IgnoresAlreadyExpiredEntries()
    {
        await _service.SetTypingAsync(_chat, _user, true);
        _time.Advance(UserStatusService.TypingTtl + TimeSpan.FromSeconds(1));

        Assert.Empty(await _service.ClearTypingAsync(_user));
    }
}
