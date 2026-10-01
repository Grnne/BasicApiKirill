namespace BasicApi.Storage.Dto;

/// <summary>Outcome of an attempt to move the read pointer.</summary>
public enum ReadPointerUpdate
{
    /// <summary>The message is not in this chat — the pointer is untouched.</summary>
    MessageNotFound,

    Moved,

    /// <summary>The pointer is already at this message or beyond — we do not move it back.</summary>
    NotMoved
}

/// <param name="FromSeq">The pointer before the move.</param>
/// <param name="ToSeq">The pointer after it; equal to <paramref name="FromSeq"/> when it did not move.</param>
/// <param name="ClearedMark">The chat was marked as unread, and reading it cleared the mark.</param>
public sealed record ReadPointerMove(ReadPointerUpdate Update, long FromSeq = 0, long ToSeq = 0, bool ClearedMark = false);

/// <summary>A member's pointer moved in a chat from one seq to another.</summary>
public sealed record PointerMove(Guid ChatId, long FromSeq, long ToSeq);

/// <summary>Which pointer: received by a device, or read.</summary>
public enum ReceiptKind
{
    Delivered,
    Read
}

/// <summary>
/// A member's view of the chat's pointers: how far they have read, and how far the other
/// members have received and read — the status of the member's own messages.
/// </summary>
public sealed class ReadPointers
{
    public long ReadSeq { get; set; }

    /// <summary>The furthest any other member has read.</summary>
    public long OutboxReadSeq { get; set; }

    /// <summary>The furthest any other member's device has received.</summary>
    public long OutboxDeliveredSeq { get; set; }

    /// <summary>False in a chat with oneself: own messages have no status there.</summary>
    public bool HasOthers { get; set; }
}
