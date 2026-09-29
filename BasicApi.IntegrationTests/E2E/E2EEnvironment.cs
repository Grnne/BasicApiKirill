using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace BasicApi.IntegrationTests.E2E;

/// <summary>
/// Сквозные тесты против развёрнутого стека (Caddy → API → Postgres) по настоящему
/// HTTPS и WebSocket. Запуск: scripts/e2e.ps1 -BaseUrl https://host.
/// Без E2E_BASE_URL тесты пропускаются — в обычном прогоне их нет.
/// </summary>
public static class E2EEnvironment
{
    public static string? BaseUrl => Environment.GetEnvironmentVariable("E2E_BASE_URL")?.TrimEnd('/');

    /// <summary>Доверять любому сертификату — для локального стека с самоподписанным.</summary>
    public static bool Insecure => Environment.GetEnvironmentVariable("E2E_INSECURE") == "1";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static HttpMessageHandler CreateHandler() => new HttpClientHandler
    {
        AllowAutoRedirect = false,
        ServerCertificateCustomValidationCallback = Insecure
            ? HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            : null
    };

    public static HttpClient CreateClient(string? token = null)
    {
        var client = new HttpClient(CreateHandler()) { BaseAddress = new Uri(BaseUrl! + "/") };
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Соединение с хабом по WebSocket — тем же транспортом, что у браузера.</summary>
    public static HubConnection CreateHub(string token) =>
        new HubConnectionBuilder()
            .WithUrl(BaseUrl + "/hubs/chat", options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                if (Insecure)
                {
                    options.HttpMessageHandlerFactory = _ => CreateHandler();
                    options.WebSocketConfiguration = ws =>
                        ws.RemoteCertificateValidationCallback = (_, _, _, _) => true;
                }
            })
            .Build();

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;
}

/// <summary>Тест, который выполняется только при заданном E2E_BASE_URL.</summary>
public sealed class E2EFactAttribute : FactAttribute
{
    public E2EFactAttribute()
    {
        if (string.IsNullOrEmpty(E2EEnvironment.BaseUrl))
            Skip = "E2E_BASE_URL is not set — run scripts/e2e.ps1";
    }
}

public sealed record E2EUser(Guid UserId, string Username, string Token, string RefreshToken);

/// <summary>
/// Два пользователя на весь прогон. Имена уникальны, поэтому прогон не зависит от
/// данных в базе. Входов/регистраций — не больше лимита auth-политики (5/мин с IP).
/// </summary>
public sealed class E2EUsers : IAsyncLifetime
{
    public const string Password = "e2e-password-123";

    public E2EUser Alice { get; private set; } = null!;
    public E2EUser Bob { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrEmpty(E2EEnvironment.BaseUrl))
            return;

        var run = Guid.NewGuid().ToString("N")[..8];
        Alice = await RegisterAsync($"E2E_Alice_{run}");
        Bob = await RegisterAsync($"e2e_bob_{run}");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public static async Task<E2EUser> RegisterAsync(string username)
    {
        using var client = E2EEnvironment.CreateClient();
        var response = await client.PostAsJsonAsync("api/auth/register",
            new { username, email = $"{username.ToLowerInvariant()}@e2e.test", password = Password });
        Assert.True(response.IsSuccessStatusCode, $"register {username}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        var body = await response.ReadAsync<JsonElement>();
        return new E2EUser(body.GetProperty("userId").GetGuid(), username,
            body.GetProperty("token").GetString()!, body.GetProperty("refreshToken").GetString()!);
    }

    public static async Task<E2EUser> LoginAsync(string login)
    {
        using var client = E2EEnvironment.CreateClient();
        var response = await client.PostAsJsonAsync("api/auth/login", new { usernameOrEmail = login, password = Password });
        Assert.True(response.IsSuccessStatusCode, $"login {login}: {(int)response.StatusCode}");
        var body = await response.ReadAsync<JsonElement>();
        return new E2EUser(body.GetProperty("userId").GetGuid(), login,
            body.GetProperty("token").GetString()!, body.GetProperty("refreshToken").GetString()!);
    }
}

[CollectionDefinition(Name)]
public sealed class E2ECollection : ICollectionFixture<E2EUsers>
{
    public const string Name = "e2e";
}
