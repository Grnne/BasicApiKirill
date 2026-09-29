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
}
