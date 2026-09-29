using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;

namespace BasicApi.Services.Events;

/// <summary>
/// События для клиентов. Доменные сервисы сообщают, что произошло, и не знают,
/// как это доставляется: названия событий хаба, группы и формат полезной нагрузки —
/// забота реализации.
/// </summary>
public interface IChatEventPublisher
{
    /// <summary>
    /// Новое сообщение: <c>MessageCreated</c> открытым чатам (группа хаба) и
    /// <c>ChatListUpdated</c> с превью — всем участникам.
    /// </summary>
    Task MessageCreatedAsync(MessageDto message, IReadOnlyCollection<Guid> memberIds, CancellationToken ct = default);

    /// <summary>
    /// Новый чат у пользователя; карточка собрана для него. <paramref name="live"/> —
    /// прислать ли <c>ChatCreated</c> сразу: создателю чата не шлём, он получил карточку
    /// в ответе, а его другие устройства узнают о чате через синхронизацию.
    /// </summary>
    Task ChatCreatedAsync(Guid recipientId, ChatListItemDto item, bool live = true, CancellationToken ct = default);

    /// <summary><c>UserOnlineChanged</c> — эфемерное событие, в журнал не пишется.</summary>
    Task UserOnlineChangedAsync(
        Guid userId, bool isOnline, IReadOnlyCollection<Guid> recipientIds, CancellationToken ct = default);

    /// <summary><c>TypingChanged</c> — эфемерное событие, в журнал не пишется.</summary>
    Task TypingChangedAsync(
        Guid chatId, Guid userId, bool isTyping, IReadOnlyCollection<Guid> recipientIds, CancellationToken ct = default);
}
