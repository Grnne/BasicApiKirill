using System.Text;

namespace BasicApi.Extensions;

/// <summary>
/// Configuration check before startup. An application with the default JWT key in prod
/// makes it possible to forge any user's token, so such a startup
/// must fail immediately and loudly, not work "somehow".
/// </summary>
public static class ConfigurationValidation
{
    /// <summary>HS256 requires a key of at least 256 bits.</summary>
    public const int MinJwtKeyBytes = 32;

    /// <summary>Keys that have ever been in the repository or templates.</summary>
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

        // Storage keys sign every upload and download link: a known one lets anyone read all files.
        if (!string.IsNullOrWhiteSpace(configuration["Storage:Endpoint"]))
        {
            var secret = configuration["Storage:SecretKey"];
            if (string.IsNullOrWhiteSpace(configuration["Storage:AccessKey"]) || string.IsNullOrWhiteSpace(secret))
                errors.Add("Storage:AccessKey and Storage:SecretKey are required when Storage:Endpoint is set.");
            else if (!environment.IsDevelopment() && secret.StartsWith("CHANGE_ME", StringComparison.Ordinal))
                errors.Add("Storage:SecretKey is a placeholder from the repository; generate a real secret.");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Invalid configuration ({environment.EnvironmentName}):" +
                string.Concat(errors.Select(e => $"{Environment.NewLine} - {e}")));
        }
    }
}
