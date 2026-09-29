namespace BasicApi.Storage.Dto;

/// <summary>Исход попытки сдвинуть указатель прочитанного.</summary>
public enum ReadPointerUpdate
{
    /// <summary>Сообщения нет в этом чате — указатель не тронут.</summary>
    MessageNotFound,

    /// <summary>Указатель сдвинут вперёд.</summary>
    Moved,

    /// <summary>Указатель уже на этом сообщении или дальше — назад не двигаем.</summary>
    NotMoved
}
