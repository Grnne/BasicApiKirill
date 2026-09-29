using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace BasicApi.IntegrationTests.E2E;

/// <summary>
/// End-to-end tests against a deployed stack (Caddy -> API -> Postgres) over real
/// HTTPS and WebSocket. Run with: scripts/e2e.ps1 -BaseUrl https://host.
/// Without E2E_BASE_URL the tests are skipped, so they are not part of a regular run.
/// </summary>
public static class E2EEnvironment
{
    public static string? BaseUrl => Environment.GetEnvironmentVariable("E2E_BASE_URL")?.TrimEnd('/');

    /// <summary>Trust any certificate, for a local stack with a self-signed one.</summary>
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

    /// <summary>Hub connection over WebSocket, the same transport a browser uses.</summary>
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

/// <summary>A test that runs only when E2E_BASE_URL is set.</summary>
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
/// Two users for the whole run. Names are unique, so the run does not depend on
/// the data already in the database. Sign-ins/registrations stay within the auth policy limit
/// (5/min per IP).
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
