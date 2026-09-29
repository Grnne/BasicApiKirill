using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace BasicApi.IntegrationTests.Infrastructure;

/// <summary>
/// Приложение целиком, в памяти, поверх тестового Postgres. Окружение — Production:
/// тесты должны видеть то же поведение, что и прод (скрытые детали ошибок и т.п.).
/// </summary>
public sealed class ApiFactory(
    string connectionString,
    IReadOnlyDictionary<string, string?>? settings = null,
    string environment = "Production",
    Action<IServiceCollection>? services = null) : WebApplicationFactory<Program>
{
    public const string JwtKey = "integration-tests-signing-key-0123456789abcdef0123456789";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
        builder.UseSetting("Jwt:Key", JwtKey);

        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
            builder.UseSetting(key, value);

        if (services is not null)
            builder.ConfigureTestServices(services);
    }
}
