using Microsoft.AspNetCore.SignalR;

namespace BasicApi.Hubs;

/// <summary>
/// Ошибки методов хаба. Клиенту доходит только текст <see cref="HubException"/>,
/// поэтому код ошибки — в начале сообщения: <c>"NOT_A_MEMBER: ..."</c>. Коды те же,
/// что у REST: доменные ошибки сервисов переводит <see cref="HubErrorFilter"/>.
/// SignalR на проводе добавляет свой префикс, и клиент получает
/// <c>"An unexpected error occurred invoking 'JoinChat' on the server. HubException: NOT_A_MEMBER: ..."</c>.
/// Код — слово сразу после <c>"HubException: "</c> (проверено E2E-тестом по WebSocket).
/// </summary>
public static class HubErrors
{
    public const string NotAMember = "NOT_A_MEMBER";
    public const string RateLimited = "RATE_LIMITED";

    public static HubException Create(string code, string message) => new($"{code}: {message}");
}
