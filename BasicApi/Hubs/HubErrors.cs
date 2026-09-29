using Microsoft.AspNetCore.SignalR;

namespace BasicApi.Hubs;

/// <summary>
/// Ошибки методов хаба. Клиенту доходит только текст <see cref="HubException"/>,
/// поэтому код ошибки — в начале сообщения: <c>"NOT_A_MEMBER: ..."</c>.
/// Клиент различает ошибки по префиксу до двоеточия, как по errorCode в REST.
/// </summary>
public static class HubErrors
{
    public const string NotAMember = "NOT_A_MEMBER";

    public static HubException Create(string code, string message) => new($"{code}: {message}");
}
