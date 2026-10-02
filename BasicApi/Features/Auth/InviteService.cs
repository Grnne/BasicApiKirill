using System.Security.Cryptography;
using System.Text;
using BasicApi.Middleware.Exceptions;
using BasicApi.Storage;
using BasicApi.Storage.Interfaces;
using Microsoft.Extensions.Options;

namespace BasicApi.Features.Auth;

/// <summary>Who may register: everyone, nobody, or the holder of a member's one-time invitation.</summary>
public sealed class InviteService(
    IDbSession db,
    IInviteRepository invites,
    IOptions<RegistrationOptions> options,
    TimeProvider time)
{
    private readonly RegistrationOptions _options = options.Value;

    public RegistrationDto Registration => new() { Mode = Mode };

    private string Mode => _options.Mode?.Trim().ToLowerInvariant() switch
    {
        RegistrationOptions.Closed => RegistrationOptions.Closed,
        RegistrationOptions.Invite => RegistrationOptions.Invite,
        _ => RegistrationOptions.Open
    };

    /// <summary>Errors: 403 <c>INVITES_DISABLED</c>, 409 <c>TOO_MANY_INVITES</c>.</summary>
    public async Task<InviteDto> CreateAsync(Guid userId, CancellationToken ct = default)
    {
        if (Mode != RegistrationOptions.Invite)
            throw new ForbiddenException("Registration is not by invitation here", "INVITES_DISABLED");
        var now = time.GetUtcNow().UtcDateTime;
        if (await invites.CountOpenAsync(userId, now, ct) >= _options.MaxOpenInvites)
            throw new ConflictException(
                $"At most {_options.MaxOpenInvites} unused invitations at once", "TOO_MANY_INVITES");

        var code = Base64Url(RandomNumberGenerator.GetBytes(16));
        var expiresAt = now.AddDays(_options.InviteDays);
        await invites.CreateAsync(Hash(code), userId, now, expiresAt, ct);
        return new InviteDto { Code = code, ExpiresAt = expiresAt };
    }

    /// <summary>
    /// Runs the registration if this instance lets the caller in. By invitation, the invitation is
    /// used up in the same transaction as the account is made: a registration that fails (a name
    /// taken) keeps it, and two with one code cannot both succeed.
    /// Errors: 403 <c>REGISTRATION_CLOSED</c>/<c>INVITE_INVALID</c>.
    /// </summary>
    public async Task<AuthResponseDto> AdmitAsync(
        string? inviteCode, Func<CancellationToken, Task<AuthResponseDto>> register, CancellationToken ct = default)
    {
        switch (Mode)
        {
            case RegistrationOptions.Closed:
                throw new ForbiddenException("Registration is closed on this server", "REGISTRATION_CLOSED");
            case RegistrationOptions.Open:
                return await register(ct);
        }

        // Checked first too: a wrong code costs no password hashing.
        var hash = string.IsNullOrWhiteSpace(inviteCode) ? null : Hash(inviteCode.Trim());
        if (hash is null || !await invites.IsOpenAsync(hash, time.GetUtcNow().UtcDateTime, ct))
            throw InvalidInvite();

        return await db.InTransactionAsync(async ct =>
        {
            var registered = await register(ct);
            if (!await invites.UseAsync(hash, registered.UserId, time.GetUtcNow().UtcDateTime, ct))
                throw InvalidInvite();
            return registered;
        }, ct: ct);
    }

    private static ForbiddenException InvalidInvite() =>
        new("The invitation is unknown, used or expired", "INVITE_INVALID");

    private static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public class RegistrationDto
{
    /// <summary><c>open</c>, <c>closed</c> or <c>invite</c> (registration then needs <c>inviteCode</c>).</summary>
    public string Mode { get; set; } = RegistrationOptions.Open;
}

public class InviteDto
{
    /// <summary>The one-time code; the server keeps only its hash, so it is shown once.</summary>
    public string Code { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
}
