import { http } from '@/shared/api/http'
import type {
  DraftDto,
  EditMessageDto,
  ForwardMessagesDto,
  ForwardMessagesResponseDto,
  MessageDto,
  MessageReactionsDto,
  SaveDraftDto,
  SendMessageDto,
} from '@/shared/api/schema'

/** 201 — stored now, 200 — a repeat of the same clientMessageId: the message stored before. */
export function sendMessage(chatId: string, body: SendMessageDto): Promise<MessageDto> {
  return http.post<MessageDto>(`/api/chats/${chatId}/messages`, body)
}

/** "Typing" fades on the server after 6 s without a repeat. */
export function sendTyping(chatId: string, isTyping: boolean): Promise<void> {
  return http.post<void>(`/api/chats/${chatId}/typing`, { isTyping })
}

/** Marks everything up to and including the given message as read. */
export function markRead(chatId: string, lastMessageId: string): Promise<void> {
  return http.post<void>(`/api/chats/${chatId}/read`, { lastMessageId })
}

/** Replaces text and formatting; the same text again is a no-op (200 without an event). */
export function editMessage(chatId: string, messageId: string, body: EditMessageDto): Promise<MessageDto> {
  return http.patch<MessageDto>(`/api/chats/${chatId}/messages/${messageId}`, body)
}

/** forEveryone: for all members (the author, within the window); otherwise only for the caller. */
export function deleteMessage(chatId: string, messageId: string, forEveryone: boolean): Promise<void> {
  return http.delete<void>(`/api/chats/${chatId}/messages/${messageId}`, forEveryone ? { query: { forEveryone } } : {})
}

/** Copies into chatId, in the source order; repeating with the same clientMessageIds creates nothing. */
export function forwardMessages(chatId: string, body: ForwardMessagesDto): Promise<ForwardMessagesResponseDto> {
  return http.post<ForwardMessagesResponseDto>(`/api/chats/${chatId}/messages/forward`, body)
}

/** One reaction per user: a new one replaces the old. Answers the summary after the change. */
export function setReaction(chatId: string, messageId: string, emoji: string): Promise<MessageReactionsDto> {
  return http.put<MessageReactionsDto>(`/api/chats/${chatId}/messages/${messageId}/reactions`, { emoji })
}

export function removeReaction(chatId: string, messageId: string): Promise<void> {
  return http.delete<void>(`/api/chats/${chatId}/messages/${messageId}/reactions`)
}

/** 200 with the draft, or 204 when the text is empty without a reply (the draft is removed). */
export async function saveDraft(chatId: string, body: SaveDraftDto): Promise<DraftDto | null> {
  return (await http.put<DraftDto | undefined>(`/api/chats/${chatId}/draft`, body)) ?? null
}

export function removeDraft(chatId: string): Promise<void> {
  return http.delete<void>(`/api/chats/${chatId}/draft`)
}
