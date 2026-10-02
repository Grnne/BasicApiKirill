// Load run against a deployed stack (docs/load-testing.md).
//
// Users and chats are created beforehand by seed.sql; their ids are md5 of the number.
// The program issues access tokens itself (it needs the stack's signing key) with the sid
// of a live sign-in from seed.sql: bcrypt logins and the auth rate limit are not measured.
//
// Phases: M hub clients connect → a stream of sends and chat list loads →
// with --restart-container, a mass reconnect after an API restart.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.IdentityModel.Tokens;

var options = Options.Parse(args);
if (options is null)
{
    Console.WriteLine(Options.Usage);
    return 2;
}

var run = new LoadRun(options);
return await run.ExecuteAsync();

sealed record Options(
    Uri BaseUrl,
    bool Insecure,
    string JwtKey,
    string Issuer,
    string Audience,
    int Users,
    int Peers,
    int Connections,
    double SendRate,
    double ListRate,
    int DurationSeconds,
    int ConnectParallelism,
    string? RestartContainer,
    string[] StatsContainers)
{
    public const string Usage = """
        Usage: BasicApi.LoadTest --jwt-key KEY [options]
          --base-url URL            default https://localhost:8443
          --insecure                do not verify the TLS certificate
          --jwt-key KEY             signing key of the stack (Jwt__Key)
          --issuer / --audience     default ChatApi / ChatClient
          --users N --peers K       what seed.sql was run with (default 2000 / 10)
          --connections M           hub clients, users 1..M (default 500)
          --send-rate R             messages per second (default 10)
          --list-rate R             GET /api/chats per second (default 5)
          --duration S              steady phase, seconds (default 60)
          --connect-parallelism P   concurrent connects (default 50)
          --restart-container NAME  after the steady phase: docker restart NAME, measure reconnect
          --stats CONTAINERS        comma-separated, sampled with docker stats (default basicchat_api,basicchat_postgres)
        """;

    public static Options? Parse(string[] args)
    {
        var map = new Dictionary<string, string?>();
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--")) return null;
            var key = args[i][2..];
            map[key] = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : null;
        }
        if (!map.TryGetValue("jwt-key", out var jwtKey) || string.IsNullOrEmpty(jwtKey)) return null;

        string Get(string key, string fallback) => map.TryGetValue(key, out var v) && v is not null ? v : fallback;

        return new Options(
            new Uri(Get("base-url", "https://localhost:8443")),
            map.ContainsKey("insecure"),
            jwtKey,
            Get("issuer", "ChatApi"),
            Get("audience", "ChatClient"),
            int.Parse(Get("users", "2000")),
            int.Parse(Get("peers", "10")),
            int.Parse(Get("connections", "500")),
            double.Parse(Get("send-rate", "10"), System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(Get("list-rate", "5"), System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(Get("duration", "60")),
            int.Parse(Get("connect-parallelism", "50")),
            map.GetValueOrDefault("restart-container"),
            Get("stats", "basicchat_api,basicchat_postgres").Split(',', StringSplitOptions.RemoveEmptyEntries));
    }
}

/// <summary>seed.sql ids: md5 of a string, same as <c>md5(...)::uuid</c> in Postgres.</summary>
static class Seed
{
    public static Guid Id(string key) => Guid.Parse(Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(key))));
    public static Guid User(int i) => Id($"load-user-{i}");
    public static Guid Family(int i) => Id($"load-family-{i}");
    public static Guid Chat(int i, int k) => Id($"load-chat-{i}-{k}");
    public static int Peer(int i, int k, int users) => (i - 1 + k) % users + 1;
}

/// <summary>Latencies of one kind of operation; thread-safe.</summary>
sealed class Metric(string name)
{
    private readonly List<double> _ms = [];
    private readonly ConcurrentDictionary<string, int> _errors = new();
    private int _windowCount;
    private double _windowMax;

    public string Name { get; } = name;

    public void Ok(double ms)
    {
        lock (_ms)
        {
            _ms.Add(ms);
            _windowCount++;
            _windowMax = Math.Max(_windowMax, ms);
        }
    }

    /// <summary>Count and max latency since the previous call, for the timeline.</summary>
    public string TakeWindow()
    {
        lock (_ms)
        {
            var result = $"{_windowCount} max {_windowMax:F0}ms";
            _windowCount = 0;
            _windowMax = 0;
            return result;
        }
    }

    public void Error(string kind) => _errors.AddOrUpdate(kind, 1, (_, n) => n + 1);

    public int Count
    {
        get { lock (_ms) return _ms.Count; }
    }

    public string Report()
    {
        double[] sorted;
        lock (_ms) sorted = [.. _ms.Order()];
        var errors = _errors.IsEmpty ? "0" : string.Join(", ", _errors.Select(e => $"{e.Key}×{e.Value}"));
        if (sorted.Length == 0) return $"{Name,-22} ok 0, errors {errors}";
        double P(double q) => sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(q * sorted.Length) - 1)];
        return $"{Name,-22} ok {sorted.Length,6}  p50 {P(0.50),7:F1}  p95 {P(0.95),7:F1}  p99 {P(0.99),7:F1}  max {sorted[^1],7:F1} ms  errors {errors}";
    }
}

sealed class LoadClient(int index, HubConnection hub)
{
    public int Index { get; } = index;
    public HubConnection Hub { get; } = hub;
    public volatile bool GaveUp;
    public long DisconnectedAt;
    public long ReconnectedAt;
    /// <summary>Where the client's journal stands; 0 — not known yet.</summary>
    public long Pts;
}

/// <summary>
/// The web client's schedule: SignalR's own attempts (hub.store.ts), then its RetryLoop (retry.ts),
/// whose last pause repeats while the user is signed in — the client never gives up.
/// </summary>
sealed class ClientRetryPolicy : IRetryPolicy
{
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
    ];

    public TimeSpan? NextRetryDelay(RetryContext context) =>
        Delays[Math.Min(context.PreviousRetryCount, Delays.Length - 1)];
}

sealed class LoadRun(Options o)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly HttpClient _http = CreateHttp(o);
    private readonly string[] _tokens = new string[o.Connections + 1];
    private readonly List<LoadClient> _clients = [];

    private readonly Metric _connect = new("hub connect");
    private readonly Metric _send = new("POST messages");
    private readonly Metric _list = new("GET /api/chats");
    private readonly Metric _delivery = new("delivery (event)");
    private readonly Metric _reconnectSync = new("sync after reconnect");
    private long _expectedDeliveries;

    public async Task<int> ExecuteAsync()
    {
        Console.WriteLine($"Target {o.BaseUrl}, {o.Connections} connections of {o.Users} users, " +
                          $"{o.SendRate}/s sends, {o.ListRate}/s chat lists, {o.DurationSeconds}s");
        if (o.Connections > o.Users || o.Peers * 2 >= o.Users)
        {
            Console.WriteLine("connections must be <= users and peers < users / 2");
            return 2;
        }

        for (var i = 1; i <= o.Connections; i++) _tokens[i] = Token(i);

        Stats("before connect");
        await ConnectAllAsync();
        Stats("connected, idle");

        await SteadyAsync();
        Stats("after steady phase");
        await Task.Delay(TimeSpan.FromSeconds(3)); // the last events are in flight

        Console.WriteLine();
        Console.WriteLine("== Steady phase ==");
        Console.WriteLine(_connect.Report());
        Console.WriteLine(_send.Report());
        Console.WriteLine(_list.Report());
        Console.WriteLine(_delivery.Report());
        Console.WriteLine($"{"deliveries",-22} {_delivery.Count} of {Interlocked.Read(ref _expectedDeliveries)} expected");

        if (o.RestartContainer is not null)
        {
            await LearnPtsAsync();
            await ReconnectStormAsync(o.RestartContainer);
        }

        await Task.WhenAll(_clients.Select(c => c.Hub.DisposeAsync().AsTask()));
        return 0;
    }

    private async Task ConnectAllAsync()
    {
        var started = _clock.Elapsed;
        using var gate = new SemaphoreSlim(o.ConnectParallelism);
        var tasks = Enumerable.Range(1, o.Connections).Select(async i =>
        {
            await gate.WaitAsync();
            try
            {
                var client = CreateClient(i);
                var t0 = _clock.Elapsed;
                try
                {
                    await client.Hub.StartAsync();
                    _connect.Ok((_clock.Elapsed - t0).TotalMilliseconds);
                    lock (_clients) _clients.Add(client);
                }
                catch (Exception ex)
                {
                    _connect.Error(Short(ex));
                    await client.Hub.DisposeAsync();
                }
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks);
        Console.WriteLine($"Connected {_clients.Count}/{o.Connections} in {(_clock.Elapsed - started).TotalSeconds:F1}s");
    }

    private LoadClient CreateClient(int i)
    {
        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(o.BaseUrl, "/hubs/chat"), options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(_tokens[i]);
                if (o.Insecure)
                {
                    options.HttpMessageHandlerFactory = _ => InsecureHandler();
                    options.WebSocketConfiguration = ws => ws.RemoteCertificateValidationCallback = (_, _, _, _) => true;
                }
            })
            .WithAutomaticReconnect(new ClientRetryPolicy())
            .Build();
        var client = new LoadClient(i, hub);

        hub.On<Guid, JsonElement>("ChatListUpdated", (_, message) =>
        {
            var text = message.GetProperty("text").GetString() ?? "";
            if (text.StartsWith("lt:") && long.TryParse(text.AsSpan(3, text.IndexOf(' ') - 3), out var sentAt))
                _delivery.Ok(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - sentAt);
        });
        hub.Reconnecting += _ =>
        {
            Interlocked.CompareExchange(ref client.DisconnectedAt, _clock.ElapsedTicks, 0);
            return Task.CompletedTask;
        };
        hub.Reconnected += async _ =>
        {
            client.ReconnectedAt = _clock.ElapsedTicks;
            // Like the web client: catch up on the journal from where it stood.
            await TimedCatchUpAsync(client);
        };
        hub.Closed += _ =>
        {
            client.GaveUp = true;
            return Task.CompletedTask;
        };
        return client;
    }

    private async Task SteadyAsync()
    {
        var connected = _clients.Select(c => c.Index).Where(i => i <= o.Connections).ToArray();
        var connectedSet = connected.ToHashSet();
        var until = _clock.Elapsed + TimeSpan.FromSeconds(o.DurationSeconds);
        var inFlight = new List<Task>();

        async Task Loop(double rate, Func<Task> action)
        {
            if (rate <= 0) return;
            var interval = TimeSpan.FromSeconds(1 / rate);
            var next = _clock.Elapsed;
            while (next < until)
            {
                var wait = next - _clock.Elapsed;
                if (wait > TimeSpan.Zero) await Task.Delay(wait);
                lock (inFlight) inFlight.Add(action());
                next += interval;
            }
        }

        Task Send()
        {
            var i = connected[Random.Shared.Next(connected.Length)];
            var k = Random.Shared.Next(1, o.Peers + 1);
            if (connectedSet.Contains(Seed.Peer(i, k, o.Users)))
                Interlocked.Increment(ref _expectedDeliveries);
            // The sender gets the new-message event too (for its other devices).
            Interlocked.Increment(ref _expectedDeliveries);
            return TimedSendAsync(i, Seed.Chat(i, k));
        }

        Task List() => TimedGetChatsAsync(connected[Random.Shared.Next(connected.Length)], _list);

        async Task Timeline()
        {
            // Every 10 s: what happened in the window and how loaded the containers are,
            // to tell a steady load from short stalls.
            while (_clock.Elapsed < until)
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                var stats = await StatsAsync();
                Console.WriteLine($"  t+{(o.DurationSeconds - (until - _clock.Elapsed).TotalSeconds):F0}s " +
                                  $"send {_send.TakeWindow()}, list {_list.TakeWindow()}, delivery {_delivery.TakeWindow()} | {stats}");
            }
        }

        Console.WriteLine($"Steady phase {o.DurationSeconds}s...");
        await Task.WhenAll(Loop(o.SendRate, Send), Loop(o.ListRate, List), Timeline());
        Task[] pending;
        lock (inFlight) pending = [.. inFlight];
        await Task.WhenAll(pending);
    }

    private async Task TimedSendAsync(int user, Guid chatId)
    {
        var body = new
        {
            text = $"lt:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()} нагрузочное сообщение для проверки доставки",
            clientMessageId = Guid.NewGuid(),
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/chats/{chatId}/messages")
        {
            Content = JsonContent.Create(body, options: Json),
        };
        await TimedAsync(request, user, _send);
    }

    /// <summary>
    /// A live web client keeps its pts current (it catches up a second after live events), so before
    /// the restart every client learns where its journal stands — untimed, it is not part of the storm.
    /// </summary>
    private async Task LearnPtsAsync()
    {
        using var gate = new SemaphoreSlim(32);
        await Task.WhenAll(_clients.Select(async client =>
        {
            await gate.WaitAsync();
            try
            {
                client.Pts = await StatePtsAsync(client.Index) ?? 0;
            }
            catch (Exception)
            {
                // Unknown pts: the catch-up after the reconnect starts from 0 and takes the snapshot.
            }
            finally
            {
                gate.Release();
            }
        }));
    }

    /// <summary>
    /// GET /api/sync?since=pts page by page until hasMore is false; snapshotRequired — GET
    /// /api/sync/state, as the web client does. Timed as a whole.
    /// </summary>
    private async Task TimedCatchUpAsync(LoadClient client)
    {
        var t0 = _clock.Elapsed;
        try
        {
            while (true)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/sync?since={client.Pts}&limit=100");
                using var response = await SendAsync(request, client.Index);
                if (!response.IsSuccessStatusCode)
                {
                    _reconnectSync.Error(((int)response.StatusCode).ToString());
                    return;
                }
                using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                var root = body.RootElement;
                if (root.GetProperty("snapshotRequired").GetBoolean())
                {
                    var pts = await StatePtsAsync(client.Index);
                    if (pts is null)
                    {
                        _reconnectSync.Error("snapshot");
                        return;
                    }
                    client.Pts = pts.Value;
                    break;
                }
                client.Pts = root.GetProperty("pts").GetInt64();
                if (!root.GetProperty("hasMore").GetBoolean()) break;
            }
            _reconnectSync.Ok((_clock.Elapsed - t0).TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _reconnectSync.Error(Short(ex));
        }
    }

    private async Task<long?> StatePtsAsync(int user)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sync/state");
        using var response = await SendAsync(request, user);
        if (!response.IsSuccessStatusCode) return null;
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return body.RootElement.GetProperty("pts").GetInt64();
    }

    private Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, int user)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokens[user]);
        return _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }

    private Task TimedGetChatsAsync(int user, Metric metric) =>
        TimedAsync(new HttpRequestMessage(HttpMethod.Get, "/api/chats"), user, metric);

    private async Task TimedAsync(HttpRequestMessage request, int user, Metric metric)
    {
        using var _ = request;
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokens[user]);
        var t0 = _clock.Elapsed;
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            await response.Content.CopyToAsync(Stream.Null);
            if (response.IsSuccessStatusCode) metric.Ok((_clock.Elapsed - t0).TotalMilliseconds);
            else metric.Error(((int)response.StatusCode).ToString());
        }
        catch (Exception ex)
        {
            metric.Error(Short(ex));
        }
    }

    private async Task ReconnectStormAsync(string container)
    {
        Console.WriteLine();
        Console.WriteLine($"== Reconnect storm: docker restart {container} ==");
        var restartAt = _clock.ElapsedTicks;
        await Docker($"restart {container}");
        Console.WriteLine($"docker restart returned after {Seconds(_clock.ElapsedTicks - restartAt):F1}s");

        // Wait until everyone reconnects; the client never gives up, so the wait is capped.
        var deadline = _clock.Elapsed + TimeSpan.FromSeconds(90);
        var nextSample = _clock.Elapsed;
        while (_clock.Elapsed < deadline &&
               _clients.Any(c => c.Hub.State != HubConnectionState.Connected && !c.GaveUp))
        {
            if (_clock.Elapsed >= nextSample)
            {
                nextSample = _clock.Elapsed + TimeSpan.FromSeconds(5);
                var connectedNow = _clients.Count(c => c.Hub.State == HubConnectionState.Connected);
                Console.WriteLine($"  t+{Seconds(_clock.ElapsedTicks - restartAt):F0}s connected {connectedNow}, " +
                                  $"catch-ups {_reconnectSync.TakeWindow()} | {await StatsAsync()}");
            }
            await Task.Delay(500);
        }
        await Task.Delay(TimeSpan.FromSeconds(3));
        Stats("after reconnect storm");

        var reconnected = _clients.Where(c => c.ReconnectedAt > 0).Select(c => Seconds(c.ReconnectedAt - restartAt)).Order().ToArray();
        var gaveUp = _clients.Count(c => c.GaveUp || c.Hub.State != HubConnectionState.Connected);
        string At(double q) => reconnected.Length == 0 ? "-" : $"{reconnected[Math.Min(reconnected.Length - 1, (int)Math.Ceiling(q * reconnected.Length) - 1)]:F1}s";
        Console.WriteLine($"reconnected {reconnected.Length}/{_clients.Count}, not connected {gaveUp}; " +
                          $"since restart: 50% {At(0.5)}, 95% {At(0.95)}, 100% {At(1)}");
        Console.WriteLine(_reconnectSync.Report());
    }

    private void Stats(string label) => Console.WriteLine($"[{label}] {StatsAsync().GetAwaiter().GetResult()}");

    private async Task<string> StatsAsync()
    {
        var output = await Docker($"stats --no-stream --format \"{{{{.Name}}}}: CPU {{{{.CPUPerc}}}}, mem {{{{.MemUsage}}}}, pids {{{{.PIDs}}}}\" {string.Join(' ', o.StatsContainers)}");
        return string.Join(" | ", output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static async Task<string> Docker(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("docker", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output;
    }

    private string Token(int i)
    {
        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, Seed.User(i).ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, $"load_{i}"),
                new Claim(JwtRegisteredClaimNames.Email, $"load_{i}@load.test"),
                new Claim(JwtRegisteredClaimNames.Sid, Seed.Family(i).ToString()),
            ]),
            Expires = DateTime.UtcNow.AddHours(2),
            Issuer = o.Issuer,
            Audience = o.Audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.JwtKey)), SecurityAlgorithms.HmacSha256),
        });
        return handler.WriteToken(token);
    }

    private static HttpClient CreateHttp(Options o) => new(o.Insecure ? InsecureHandler() : new SocketsHttpHandler
    {
        MaxConnectionsPerServer = 256,
    })
    {
        BaseAddress = o.BaseUrl,
        Timeout = TimeSpan.FromSeconds(30),
    };

    private static HttpMessageHandler InsecureHandler() => new SocketsHttpHandler
    {
        MaxConnectionsPerServer = 256,
        SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true },
    };

    private double Seconds(long ticks) => (double)ticks / Stopwatch.Frequency;

    private static string Short(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: { } code } => ((int)code).ToString(),
        TaskCanceledException => "timeout",
        _ => ex.GetType().Name,
    };
}
