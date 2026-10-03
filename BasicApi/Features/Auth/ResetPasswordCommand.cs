namespace BasicApi.Features.Auth;

/// <summary>
/// <c>--reset-password &lt;login&gt;</c>: the administrator gives a user who forgot the password a
/// temporary one. Runs instead of the server, against the same database.
/// </summary>
public static class ResetPasswordCommand
{
    public const string Flag = "--reset-password";

    /// <summary>The command is asked for: the server does not start.</summary>
    public static bool IsRequested(string[] args) => args.Contains(Flag);

    /// <summary>Exit code: 0 — reset, 1 — no such active user, 2 — no login given.</summary>
    public static async Task<int> RunAsync(
        IServiceProvider services, string[] args, TextWriter output, TextWriter errors, CancellationToken ct = default)
    {
        var at = Array.IndexOf(args, Flag);
        var username = at + 1 < args.Length ? args[at + 1].Trim() : string.Empty;
        if (username.Length == 0 || username.StartsWith("--", StringComparison.Ordinal))
        {
            await errors.WriteLineAsync($"Usage: {Flag} <login>");
            return 2;
        }

        await using var scope = services.CreateAsyncScope();
        var password = await scope.ServiceProvider.GetRequiredService<AuthService>().ResetPasswordAsync(username, ct);
        if (password is null)
        {
            await errors.WriteLineAsync($"No active user with the login {username}");
            return 1;
        }

        await output.WriteLineAsync($"Temporary password for {username}: {password}");
        await output.WriteLineAsync("Every sign-in of the user is ended. Sign in with it and change the password in the profile.");
        return 0;
    }
}
