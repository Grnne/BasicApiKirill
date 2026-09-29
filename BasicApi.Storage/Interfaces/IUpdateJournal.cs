using BasicApi.Storage.Dto;

namespace BasicApi.Storage.Interfaces;

/// <summary>
/// Журнал изменений пользователя: у каждого своя нумерация pts без пропусков
/// (номер выдаётся в той же транзакции, что и запись; откат не оставляет дыр).
/// </summary>
public interface IUpdateJournal
{
    /// <summary>Добавляет одно и то же изменение в журналы нескольких пользователей.</summary>
    Task AppendAsync(IReadOnlyCollection<Guid> userIds, string type, string payloadJson, CancellationToken ct = default);

    /// <summary>Последний pts пользователя; 0 — журнал пуст.</summary>
    Task<long> GetPtsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Изменения с pts больше <paramref name="sincePts"/>, по порядку.</summary>
    Task<IReadOnlyList<UserUpdate>> GetSinceAsync(Guid userId, long sincePts, int limit, CancellationToken ct = default);

    /// <summary>Запоминает, что устройство получило журнал до <paramref name="pts"/>; назад не двигается.</summary>
    Task AckAsync(Guid userId, Guid sessionFamilyId, long pts, CancellationToken ct = default);

    /// <summary>
    /// Удаляет записи журнала старше <paramref name="olderThan"/> — не больше
    /// <paramref name="batchSize"/> за раз, чтобы не держать долгих блокировок.
    /// </summary>
    Task<int> DeleteOlderThanAsync(DateTime olderThan, int batchSize, CancellationToken ct = default);
}
