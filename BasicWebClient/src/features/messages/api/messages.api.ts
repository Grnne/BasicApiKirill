import { http } from '@/shared/api/http'
import type { MessageDto, SendMessageDto } from '@/shared/api/schema'

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
