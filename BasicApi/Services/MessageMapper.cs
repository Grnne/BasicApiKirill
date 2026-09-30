using BasicApi.Models;
using BasicApi.Models.Dto.Message;
using BasicApi.Services.Events;
using BasicApi.Storage.Dto;

namespace BasicApi.Services;

/// <summary>A stored message as clients see it; the viewer-specific parts are filled elsewhere.</summary>
public static class MessageMapper
{
    public static MessageDto Map(MessageWithSender m) => new()
    {
        Id = m.Id,
        ChatId = m.ChatId,
        SenderId = m.SenderId,
        SenderName = m.SenderName,
        Text = m.Text,
        CreatedAt = m.CreatedAt,
        IsRead = false, // for the viewer: MapForViewerAsync
        Seq = m.Seq,
        ClientMessageId = m.ClientMessageId,
        Type = m.Type,
        EditedAt = m.EditedAt,
        Entities = MessageEntities.Deserialize(m.EntitiesJson),
        Reactions = ReactionService.Summary(m.ReactionsJson),
        MyReaction = m.MyReaction,
        ReplyTo = m.ReplyToMessageId is { } replyId
            ? new MessageReplyDto
            {
                MessageId = replyId,
                SenderId = m.ReplyToSenderId ?? Guid.Empty,
                SenderName = m.ReplyToSenderName ?? "Unknown",
                Text = m.ReplyToDeleted ? string.Empty : Preview(m.ReplyToText),
                Deleted = m.ReplyToDeleted
            }
            : null,
        ForwardFrom = m.IsForward
            ? new MessageForwardDto
            {
                SenderId = m.ForwardFromUserId ?? Guid.Empty,
                SenderName = m.ForwardFromUserName ?? "Unknown"
            }
            : null,
        Action = SystemMessages.Read(m.ContentJson),
        Attachments = Media.MessageAttachments.Read(m.AttachmentsJson)
    };

    private static string Preview(string? text) =>
        text is null ? string.Empty
        : text.Length > SignalRChatEventPublisher.PreviewLength ? text[..SignalRChatEventPublisher.PreviewLength] + "…"
        : text;
}
