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

/// <summary>Registration/login response — only what the tests need.</summary>
public sealed record AuthResult(Guid UserId, string Token, string RefreshToken);

/// <summary>API and hub calls on behalf of a test user.</summary>
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
    /// Hub connection through TestServer. Long polling: TestServer supports WebSocket
    /// less well, and for connection lifecycle checks the transport does not matter.
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

    /// <summary>Waits for the connection to close; false if it did not close in time.</summary>
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

    /// <summary>Access token with a given lifetime, signed with the test application's key.</summary>
    /// <param name="sessionFamilyId">The login the token belongs to (claim <c>sid</c>); without it, a token without a session.</param>
    public static string ShortLivedToken(Guid userId, TimeSpan lifetime, Guid? sessionFamilyId = null)
    {
        List<Claim> claims = [new(JwtRegisteredClaimNames.Sub, userId.ToString())];
        if (sessionFamilyId is not null)
            claims.Add(new Claim(JwtRegisteredClaimNames.Sid, sessionFamilyId.Value.ToString()));

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.Add(lifetime),
            Issuer = "ChatApi",
            Audience = "ChatClient",
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.JwtKey)), SecurityAlgorithms.HmacSha256)
        });
        return handler.WriteToken(token);
    }

    /// <summary>The login (claim <c>sid</c>) the access token was issued to.</summary>
    public static Guid SessionFamilyOf(string token) =>
        Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(token).Claims
            .First(c => c.Type == JwtRegisteredClaimNames.Sid).Value);
}
