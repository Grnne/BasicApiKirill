using System.Text;

namespace BasicApi.Extensions;

/// <summary>
/// Проверка конфигурации до старта. Приложение с дефолтным JWT-ключом в проде —
/// это возможность подделать токен любого пользователя, поэтому такой старт
/// должен падать сразу и громко, а не работать «как-нибудь».
/// </summary>
public static class ConfigurationValidation
{
    /// <summary>HS256 требует ключ не короче 256 бит.</summary>
    public const int MinJwtKeyBytes = 32;

    /// <summary>Ключи, которые когда-либо лежали в репозитории или шаблонах.</summary>
    private static readonly string[] KnownPlaceholderKeys =
    [
        "your-super-secret-key-with-at-least-32-characters-long",
        "dev-only-signing-key-do-not-use-in-production-0123456789",
    ];

    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        var errors = new List<string>();

        var key = configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key))
        {
            errors.Add("Jwt:Key is not configured.");
        }
        else
        {
            if (Encoding.UTF8.GetByteCount(key) < MinJwtKeyBytes)
                errors.Add($"Jwt:Key must be at least {MinJwtKeyBytes} bytes.");

            if (!environment.IsDevelopment() &&
                (KnownPlaceholderKeys.Contains(key) || key.StartsWith("CHANGE_ME", StringComparison.Ordinal)))
            {
                errors.Add("Jwt:Key is a placeholder from the repository; generate a real secret " +
                           "(openssl rand -base64 48).");
            }
        }

        if (string.IsNullOrWhiteSpace(configuration["Jwt:Issuer"]))
            errors.Add("Jwt:Issuer is not configured.");
        if (string.IsNullOrWhiteSpace(configuration["Jwt:Audience"]))
            errors.Add("Jwt:Audience is not configured.");

        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection")))
            errors.Add("ConnectionStrings:DefaultConnection is not configured.");

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Invalid configuration ({environment.EnvironmentName}):" +
                string.Concat(errors.Select(e => $"{Environment.NewLine} - {e}")));
        }
    }
}
