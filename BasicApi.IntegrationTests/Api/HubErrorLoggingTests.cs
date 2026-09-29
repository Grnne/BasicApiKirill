using System.Collections.Concurrent;
using BasicApi.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BasicApi.IntegrationTests.Api;

/// <summary>
/// Ошибка клиента в хабе (чужой чат, пустое сообщение) — не ошибка сервера: как и 4xx
/// в REST, она не должна попадать в лог уровнем Error. Найдено E2E-прогоном: SignalR
/// сам пишет Error на каждый HubException.
/// </summary>
public class HubErrorLoggingTests(PostgresFixture db) : DbTest(db)
{
    [Fact]
    public async Task ClientErrorsInTheHub_AreNotLoggedAsErrors()
    {
        var logs = new CollectingLoggerProvider();
        await using var factory = new ApiFactory(Db.ConnectionString,
            services: s => s.AddSingleton<ILoggerProvider>(logs));
        var alice = await factory.RegisterAsync("alice");
        await using var hub = factory.CreateHubConnection(alice.Token);
        await hub.StartAsync();

        await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync("JoinChat", Guid.NewGuid()));
        await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync("SendMessage", Guid.NewGuid(), "   "));

        Assert.Empty(logs.Entries.Where(e => e.Level >= LogLevel.Error).Select(e => $"{e.Category}: {e.Message}"));
    }

    private sealed class CollectingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(string Category, LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public void Dispose() { }

        private sealed class Logger(CollectingLoggerProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                owner.Entries.Enqueue((category, logLevel, formatter(state, exception)));
        }
    }
}
