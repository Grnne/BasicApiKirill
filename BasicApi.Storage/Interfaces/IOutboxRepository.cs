using BasicApi.Storage.Dto;

namespace BasicApi.Storage.Interfaces;

public interface IOutboxRepository
{
    /// <summary>Кладёт событие в outbox — в текущей транзакции, если она открыта.</summary>
    Task EnqueueAsync(string type, string payloadJson, CancellationToken ct = default);

    /// <summary>
    /// Неразосланные события по порядку, с блокировкой строк до конца транзакции
    /// (SKIP LOCKED: второй диспетчер возьмёт другие). Вызывать в транзакции.
    /// </summary>
    Task<IReadOnlyList<OutboxRow>> LockPendingAsync(int limit, CancellationToken ct = default);

    Task MarkProcessedAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default);

    /// <summary>Неудачная попытка рассылки; после <paramref name="giveUpAfter"/> событие снимается.</summary>
    Task<int> MarkFailedAsync(long id, int giveUpAfter, CancellationToken ct = default);
}
