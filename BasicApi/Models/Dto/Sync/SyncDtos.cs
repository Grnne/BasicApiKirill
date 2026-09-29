using System.Text.Json;
using BasicApi.Models.Dto.Chat;

namespace BasicApi.Models.Dto.Sync;

/// <summary>Снимок: всё, что нужно клиенту, чтобы начать с чистого листа.</summary>
public class SyncStateDto
{
    /// <summary>Номер последнего изменения, учтённого в снимке. С него — <c>GET /api/sync?since=</c>.</summary>
    public long Pts { get; set; }

    /// <summary>Список чатов — как <c>GET /api/chats</c>, со счётчиками непрочитанных.</summary>
    public List<ChatListItemDto> Chats { get; set; } = [];
}

/// <summary>Изменения после известного клиенту pts.</summary>
public class SyncDifferenceDto
{
    /// <summary>Изменения по порядку pts.</summary>
    public List<SyncUpdateDto> Updates { get; set; } = [];

    /// <summary>pts, до которого клиент теперь в курсе: передать в следующий запрос как <c>since</c>.</summary>
    public long Pts { get; set; }

    /// <summary>Есть ещё изменения — повторить запрос с новым <c>since</c>.</summary>
    public bool HasMore { get; set; }

    /// <summary>
    /// Разницы нет: часть изменений уже удалена из журнала (клиент не заходил дольше
    /// срока хранения) или <c>since</c> не из этого журнала. Нужно заново взять снимок
    /// <c>GET /api/sync/state</c>.
    /// </summary>
    public bool SnapshotRequired { get; set; }
}

public class SyncUpdateDto
{
    public long Pts { get; set; }

    /// <summary>Имя события хаба: <c>MessageCreated</c>, <c>ChatCreated</c>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Тот же объект, что приходит в событии хаба с этим именем.</summary>
    public JsonElement Payload { get; set; }

    public DateTime CreatedAt { get; set; }
}

public class SyncAckDto
{
    /// <summary>pts последнего изменения, которое устройство получило и обработало.</summary>
    public long Pts { get; set; }
}
