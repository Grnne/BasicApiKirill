using BasicApi.Models.Dto.Chat;
using BasicApi.Models.Dto.Message;
using BasicApi.Storage.Dto;

namespace BasicApi.Services;

/// <summary>
/// Единственное место, где строка списка чатов из БД превращается в DTO.
/// Раньше маппинг был продублирован в списке чатов и в поиске, из-за чего
/// поиск терял CompanionId/CompanionUsername.
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
        LastActivityAt = r.LastMessageCreatedAt ?? r.CreatedAt,
        LastMessage = r.LastMessageId is not null ? new MessageDto
        {
            Id = r.LastMessageId!.Value,
            ChatId = r.ChatId,
            SenderId = r.LastMessageSenderId!.Value,
            SenderName = r.LastMessageSenderName ?? "Unknown",
            Text = r.LastMessageText ?? string.Empty,
            CreatedAt = r.LastMessageCreatedAt!.Value,
            IsRead = false
        } : null
    };
}
