using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.IdentityModel.Tokens;

namespace BasicApi.IntegrationTests.Infrastructure;

/// <summary>Ответ регистрации/входа — только то, что нужно тестам.</summary>
public sealed record AuthResult(Guid UserId, string Token, string RefreshToken);

/// <summary>Вызовы API и хаба от имени тестового пользователя.</summary>
public static class ApiClient
{
    public const string Password = "secret123";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<AuthResult> RegisterAsync(this ApiFactory factory, string username, string? email = null)
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { username, email = email ?? $"{username}@test.local", password = Password });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthResult>(Json))!;
    }

    public static async Task<AuthResult> LoginAsync(this ApiFactory factory, string usernameOrEmail)
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { usernameOrEmail, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AuthResult>(Json))!;
    }

    public static HttpClient CreateClient(this ApiFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Соединение с хабом через TestServer. Long polling: WebSocket TestServer
    /// поддерживает хуже, а для проверок жизненного цикла соединения транспорт не важен.
    /// </summary>
    public static HubConnection CreateHubConnection(this ApiFactory factory, string token) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/chat"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();

    /// <summary>Ждёт закрытия соединения; false — не закрылось за отведённое время.</summary>
    public static async Task<bool> WaitForCloseAsync(this HubConnection connection, TimeSpan timeout)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };
        if (connection.State == HubConnectionState.Disconnected) return true;
        return await Task.WhenAny(closed.Task, Task.Delay(timeout)) == closed.Task;
    }

    /// <summary>Access-токен с заданным временем жизни, подписанный ключом тестового приложения.</summary>
    public static string ShortLivedToken(Guid userId, TimeSpan lifetime)
    {
        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())]),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.Add(lifetime),
            Issuer = "ChatApi",
            Audience = "ChatClient",
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.JwtKey)), SecurityAlgorithms.HmacSha256)
        });
        return handler.WriteToken(token);
    }
}
