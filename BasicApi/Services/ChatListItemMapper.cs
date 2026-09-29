using BasicApi.Models;
using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;
using BasicApi.Storage.Dto;

namespace BasicApi.Services;

/// <summary>
/// The only place where a chat list row from the DB is turned into a DTO.
/// The mapping used to be duplicated in the chat list and in search, which is why
/// search lost CompanionId/CompanionUsername.
/// </summary>
public static class ChatListItemMapper
{
    public static ChatListItemDto Map(ChatListResult r) => new()
    {
        ChatId = r.ChatId,
        Type = r.Type,
        Title = r.Title,
        CompanionId = r.CompanionId,
        CompanionName = r.CompanionName,
        CompanionUsername = r.CompanionUsername,
        UnreadCount = r.UnreadCount,
        UnreadMentionCount = r.UnreadMentionCount,
        LastReadSeq = r.LastReadSeq,
        MarkedUnread = r.MarkedUnread,
        OutboxReadSeq = r.OutboxReadSeq,
        OutboxDeliveredSeq = r.OutboxDeliveredSeq,
        LastActivityAt = r.LastActivityAt,
        Draft = r.DraftUpdatedAt is { } draftUpdatedAt
            ? new DraftDto
            {
                Text = r.DraftText ?? string.Empty,
                Entities = MessageEntities.Deserialize(r.DraftEntitiesJson),
                ReplyToMessageId = r.DraftReplyToMessageId,
                UpdatedAt = draftUpdatedAt
            }
            : null,
        LastMessage = r.LastMessageId is not null ? new MessageDto
        {
            Id = r.LastMessageId!.Value,
            ChatId = r.ChatId,
            SenderId = r.LastMessageSenderId!.Value,
            SenderName = r.LastMessageSenderName ?? "Unknown",
            Text = r.LastMessageText ?? string.Empty,
            CreatedAt = r.LastMessageCreatedAt!.Value,
            IsRead = r.LastMessageIsOwn
                ? r.LastMessageSeq <= r.OutboxReadSeq && r.HasOthers
                : r.LastMessageSeq <= r.LastReadSeq,
            Status = r.LastMessageIsOwn
                ? MessageStatuses.OfOwn(r.LastMessageSeq ?? 0, r.HasOthers, r.OutboxReadSeq, r.OutboxDeliveredSeq)
                : null,
            Seq = r.LastMessageSeq ?? 0
        } : null
    };
}
