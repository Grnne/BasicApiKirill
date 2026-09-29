using BasicApi.Middleware.Exceptions;

namespace BasicApi.Services;

/// <summary>
/// Решение политики: разрешено или нет, и почему — код уходит клиенту как есть
/// (REST — 403 с этим errorCode, хаб — <c>"CODE: message"</c>).
/// </summary>
public sealed record PolicyDecision(bool Allowed, string? Code = null, string? Reason = null)
{
    public static readonly PolicyDecision Allow = new(true);
    public static PolicyDecision Deny(string code, string reason) => new(false, code, reason);
}

/// <summary>
/// Единственное место, где решается, кто что может в чате. Сервисы спрашивают
/// политику и не проверяют членство сами. Сейчас правило одно — «участник чата»;
/// права в группах, блокировки и настройки приватности (план 2) встанут сюда,
/// не трогая вызывающий код. Права на правку сообщений и управление участниками
/// появятся вместе с этими функциями.
/// </summary>
public interface IChatPolicy
{
    /// <summary>Читать историю, искать, видеть состав, подписываться на события чата, отмечать прочитанное.</summary>
    Task<PolicyDecision> CanReadAsync(Guid userId, Guid chatId, CancellationToken ct = default);

    /// <summary>Писать сообщения и показывать «печатает».</summary>
    Task<PolicyDecision> CanPostAsync(Guid userId, Guid chatId, CancellationToken ct = default);

    /// <summary>Кто видит онлайн-статус пользователя — им рассылаются его изменения.</summary>
    Task<IReadOnlyCollection<Guid>> GetPresenceAudienceAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Чей онлайн-статус из <paramref name="userIds"/> виден <paramref name="viewerId"/>.</summary>
    Task<IReadOnlySet<Guid>> FilterPresenceVisibleAsync(
        Guid viewerId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);
}

public sealed class ChatPolicy(IMembershipService membership) : IChatPolicy
{
    public const string NotAMemberCode = "NOT_A_MEMBER";
    private const string NotAMemberReason = "User is not a member of this chat";

    public Task<PolicyDecision> CanReadAsync(Guid userId, Guid chatId, CancellationToken ct = default) =>
        MemberOnlyAsync(userId, chatId, ct);

    public Task<PolicyDecision> CanPostAsync(Guid userId, Guid chatId, CancellationToken ct = default) =>
        MemberOnlyAsync(userId, chatId, ct);

    public async Task<IReadOnlyCollection<Guid>> GetPresenceAudienceAsync(Guid userId, CancellationToken ct = default) =>
        await membership.GetContactIdsAsync(userId, ct);

    public async Task<IReadOnlySet<Guid>> FilterPresenceVisibleAsync(
        Guid viewerId, IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
    {
        // Свой статус виден всегда; чужой — если есть общий чат.
        var visible = (await membership.GetContactIdsAsync(viewerId, ct)).ToHashSet();
        visible.Add(viewerId);
        return userIds.Where(visible.Contains).ToHashSet();
    }

    private async Task<PolicyDecision> MemberOnlyAsync(Guid userId, Guid chatId, CancellationToken ct) =>
        await membership.IsMemberAsync(chatId, userId, ct)
            ? PolicyDecision.Allow
            : PolicyDecision.Deny(NotAMemberCode, NotAMemberReason);
}

public static class ChatPolicyExtensions
{
    /// <summary>Бросает 403 с кодом из решения политики, если чтение запрещено.</summary>
    public static async Task DemandReadAsync(this IChatPolicy policy, Guid userId, Guid chatId, CancellationToken ct = default) =>
        Demand(await policy.CanReadAsync(userId, chatId, ct));

    /// <summary>Бросает 403 с кодом из решения политики, если писать запрещено.</summary>
    public static async Task DemandPostAsync(this IChatPolicy policy, Guid userId, Guid chatId, CancellationToken ct = default) =>
        Demand(await policy.CanPostAsync(userId, chatId, ct));

    private static void Demand(PolicyDecision decision)
    {
        if (!decision.Allowed)
            throw new ForbiddenException(decision.Reason ?? "Access denied", decision.Code ?? "ACCESS_DENIED");
    }
}
