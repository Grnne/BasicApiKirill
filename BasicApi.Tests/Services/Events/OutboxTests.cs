using System.Text.Json;
using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;
using BasicApi.Services.Events;
using BasicApi.Storage;
using BasicApi.Storage.Dto;
using BasicApi.Storage.Interfaces;
using BasicApi.Tests.TestDoubles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Moq;

namespace BasicApi.Tests.Services.Events;

/// <summary>Outbox в памяти: то, что публикатор кладёт, диспетчер забирает.</summary>
internal sealed class InMemoryOutbox : IOutboxRepository
{
    public List<(long Id, string Type, string Payload, int Attempts, bool Processed)> Rows { get; } = [];

    public Task EnqueueAsync(string type, string payloadJson, CancellationToken ct = default)
    {
        Rows.Add((Rows.Count + 1, type, payloadJson, 0, false));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OutboxRow>> LockPendingAsync(int limit, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<OutboxRow>>([.. Rows.Where(r => !r.Processed).Take(limit)
            .Select(r => new OutboxRow(r.Id, r.Type, r.Payload, r.Attempts))]);

    public Task MarkProcessedAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default)
    {
        for (var i = 0; i < Rows.Count; i++)
            if (ids.Contains(Rows[i].Id)) Rows[i] = Rows[i] with { Processed = true };
        return Task.CompletedTask;
    }

    public Task<int> MarkFailedAsync(long id, int giveUpAfter, CancellationToken ct = default)
    {
        var i = Rows.FindIndex(r => r.Id == id);
        var attempts = Rows[i].Attempts + 1;
        Rows[i] = Rows[i] with { Attempts = attempts, Processed = attempts >= giveUpAfter };
        return Task.FromResult(attempts);
    }

    public Task<int> DeleteProcessedOlderThanAsync(DateTime olderThan, int batchSize, CancellationToken ct = default) =>
        Task.FromResult(0);
}

public class OutboxTests
{
    private readonly FakeDbSession _db = new();
    private readonly InMemoryOutbox _outbox = new();
    private readonly Mock<IUpdateJournal> _journalMock = new();
    private readonly OutboxSignal _signal = new();
    private readonly RecordingHubContext _hub = new();
    private readonly FakeLogger<OutboxDispatcher> _logger = new();
    private readonly OutboxChatEventPublisher _publisher;
    private readonly OutboxDispatcher _dispatcher;

    public OutboxTests()
    {
        _publisher = new OutboxChatEventPublisher(_db, _outbox, _journalMock.Object, _signal, new SignalRChatEventPublisher(_hub));

        var services = new ServiceCollection()
            .AddScoped<IDbSession>(_ => _db)
            .AddScoped<IOutboxRepository>(_ => _outbox)
            .BuildServiceProvider();
        _dispatcher = new OutboxDispatcher(services.GetRequiredService<IServiceScopeFactory>(), _hub, _signal,
            new ConfigurationBuilder().Build(), _logger);
    }

    private static MessageDto Message(string text = "hello") => new()
    {
        Id = Guid.NewGuid(),
        ChatId = Guid.NewGuid(),
        SenderId = Guid.NewGuid(),
        SenderName = "Alice",
        Text = text,
        CreatedAt = new DateTime(2026, 9, 29, 18, 4, 5, DateTimeKind.Utc),
        Seq = 42,
        ClientMessageId = Guid.NewGuid()
    };

    private static string Json(object? value) => JsonSerializer.Serialize(value, OutboxEnvelope.Json);

    [Fact]
    public async Task MessageCreated_IsJournaledAndQueued_NotSentDirectly()
    {
        var message = Message();
        Guid[] members = [message.SenderId, Guid.NewGuid()];

        await _publisher.MessageCreatedAsync(message, members);

        _journalMock.Verify(j => j.AppendAsync(members, UpdateTypes.MessageCreated,
            It.Is<string>(p => p.Contains(message.Id.ToString())), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(_outbox.Rows);
        Assert.Empty(_hub.Recorder.Sent);
    }

    [Fact]
    public async Task Dispatched_Events_AreTheSameOnTheWire_AsDirectSends()
    {
        // Клиент не должен заметить, что события теперь идут через outbox.
        var message = Message(new string('x', 150));
        Guid[] members = [message.SenderId, Guid.NewGuid()];
        var recipient = Guid.NewGuid();
        var chat = new ChatListItemDto { ChatId = Guid.NewGuid(), Type = "private", CompanionId = Guid.NewGuid() };

        var direct = new RecordingHubContext();
        var directPublisher = new SignalRChatEventPublisher(direct);
        await directPublisher.MessageCreatedAsync(message, members);
        await directPublisher.ChatCreatedAsync(recipient, chat);

        await _publisher.MessageCreatedAsync(message, members);
        await _publisher.ChatCreatedAsync(recipient, chat);
        await _dispatcher.DispatchPendingAsync();

        Assert.Equal(direct.Recorder.Sent.Count, _hub.Recorder.Sent.Count);
        foreach (var (expected, actual) in direct.Recorder.Sent.Zip(_hub.Recorder.Sent))
        {
            Assert.Equal((expected.Target, expected.Method), (actual.Target, actual.Method));
            Assert.Equal(expected.Ids, actual.Ids);
            Assert.Equal(expected.Args.Select(Json), actual.Args.Select(Json));
        }
    }

    [Fact]
    public async Task ChatCreated_NotLive_IsOnlyJournaled()
    {
        var owner = Guid.NewGuid();

        await _publisher.ChatCreatedAsync(owner, new ChatListItemDto { ChatId = Guid.NewGuid() }, live: false);

        _journalMock.Verify(j => j.AppendAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == owner), UpdateTypes.ChatCreated,
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(_outbox.Rows);
    }

    [Fact]
    public async Task TypingAndOnline_AreEphemeral_SentAtOnce_NotJournaled()
    {
        await _publisher.TypingChangedAsync(Guid.NewGuid(), Guid.NewGuid(), true, [Guid.NewGuid()]);
        await _publisher.UserOnlineChangedAsync(Guid.NewGuid(), true, [Guid.NewGuid()]);

        Assert.Equal(["TypingChanged", "UserOnlineChanged"], _hub.Recorder.Sent.Select(s => s.Method));
        Assert.Empty(_outbox.Rows);
        _journalMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Publisher_WakesTheDispatcher_OnlyAfterCommit()
    {
        await _db.InTransactionAsync(async ct =>
        {
            await _publisher.MessageCreatedAsync(Message(), [Guid.NewGuid()], ct);
            Assert.False(await _signal.WaitAsync(TimeSpan.Zero, CancellationToken.None)); // ещё не закоммичено
            return true;
        });

        Assert.True(await _signal.WaitAsync(TimeSpan.Zero, CancellationToken.None));
    }

    [Fact]
    public async Task Dispatcher_SendsInOrder_AndMarksProcessed()
    {
        for (var i = 0; i < 3; i++)
            await _publisher.ChatCreatedAsync(Guid.NewGuid(), new ChatListItemDto { Title = $"c{i}" });

        var result = await _dispatcher.DispatchPendingAsync();

        Assert.Equal(new DispatchResult(3, Failed: false), result);
        Assert.All(_outbox.Rows, r => Assert.True(r.Processed));
        Assert.Equal(["c0", "c1", "c2"],
            _hub.Recorder.Sent.Select(s => ((JsonElement)s.Args[0]!).GetProperty("title").GetString()));
        Assert.Equal(new DispatchResult(0, Failed: false), await _dispatcher.DispatchPendingAsync());
    }

    [Fact]
    public async Task Dispatcher_OnFailure_StopsToKeepOrder_AndRetriesLater()
    {
        await _publisher.ChatCreatedAsync(Guid.NewGuid(), new ChatListItemDto { Title = "ok" });
        await _outbox.EnqueueAsync("Broken", "{\"sends\":[{\"target\":\"nowhere\",\"ids\":[],\"method\":\"X\",\"args\":[]}]}");
        await _publisher.ChatCreatedAsync(Guid.NewGuid(), new ChatListItemDto { Title = "after" });

        var result = await _dispatcher.DispatchPendingAsync();

        Assert.Equal(new DispatchResult(1, Failed: true), result);
        Assert.Equal([true, false, false], _outbox.Rows.Select(r => r.Processed));
        Assert.Equal(1, _outbox.Rows[1].Attempts);
        Assert.Equal(LogLevel.Warning, _logger.LatestRecord.Level);
    }

    [Fact]
    public async Task Dispatcher_GivesUpOnAPoisonEvent_AndMovesOn()
    {
        await _outbox.EnqueueAsync("Broken", "not json");
        await _publisher.ChatCreatedAsync(Guid.NewGuid(), new ChatListItemDto { Title = "next" });

        for (var i = 0; i < OutboxDispatcher.MaxAttempts; i++)
            await _dispatcher.DispatchPendingAsync();

        Assert.True(_outbox.Rows[0].Processed);
        Assert.Equal(LogLevel.Error, _logger.LatestRecord.Level);
        Assert.Equal(new DispatchResult(1, Failed: false), await _dispatcher.DispatchPendingAsync());
    }
}
