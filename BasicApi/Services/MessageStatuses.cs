using BasicApi.Models.Dto.Message;
using BasicApi.Storage.Dto;

namespace BasicApi.Services;

/// <summary>
/// Status of a message for the one who looks at it (D1): of one's own — how far the other members
/// got; of someone else's — whether the viewer has read it.
/// </summary>
public static class MessageStatuses
{
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Read = "read";

    /// <summary>Status of the viewer's own message; null in a chat with oneself.</summary>
    public static string? OfOwn(long seq, bool hasOthers, long outboxReadSeq, long outboxDeliveredSeq) =>
        !hasOthers ? null
        : seq <= outboxReadSeq ? Read
        : seq <= outboxDeliveredSeq ? Delivered
        : Sent;

    /// <summary>Fills <see cref="MessageDto.Status"/> and <see cref="MessageDto.IsRead"/> for the viewer.</summary>
    public static void Apply(MessageDto message, Guid viewerId, ReadPointers pointers)
    {
        if (message.SenderId == viewerId)
        {
            message.Status = OfOwn(message.Seq, pointers.HasOthers, pointers.OutboxReadSeq, pointers.OutboxDeliveredSeq);
            message.IsRead = message.Status == Read;
        }
        else
        {
            message.IsRead = message.Seq <= pointers.ReadSeq;
        }
    }
}
