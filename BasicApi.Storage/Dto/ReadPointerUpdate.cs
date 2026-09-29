namespace BasicApi.Storage.Dto;

/// <summary>Outcome of an attempt to move the read pointer.</summary>
public enum ReadPointerUpdate
{
    /// <summary>The message is not in this chat — the pointer is untouched.</summary>
    MessageNotFound,

    /// <summary>The pointer moved forward.</summary>
    Moved,

    /// <summary>The pointer is already at this message or beyond — we do not move it back.</summary>
    NotMoved
}
