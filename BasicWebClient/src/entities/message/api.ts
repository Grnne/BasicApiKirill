import { http } from '@/shared/api/http'
import type { MessageWindowDto } from '@/shared/api/schema'
import type { MessagePage } from './types'

/** Server cap is 100. */
export const PAGE_SIZE = 30

/** Without a cursor — the newest page; nextCursor leads back in time. Items are oldest first. */
export function getMessagesPage(chatId: string, cursor: string | null, signal?: AbortSignal): Promise<MessagePage> {
  return http.get<MessagePage>(`/api/chats/${chatId}/messages/cursor`, {
    query: { limit: PAGE_SIZE, cursor: cursor ?? undefined },
    ...(signal ? { signal } : {}),
  })
}

/** Half a page before the message, the message, the rest after it. */
export function getMessageContext(chatId: string, messageId: string): Promise<MessageWindowDto> {
  return http.get<MessageWindowDto>(`/api/chats/${chatId}/messages/${messageId}/context`, {
    query: { limit: PAGE_SIZE },
  })
}

export function getMessagesAfter(chatId: string, seq: number): Promise<MessageWindowDto> {
  return http.get<MessageWindowDto>(`/api/chats/${chatId}/messages/after`, { query: { seq, limit: PAGE_SIZE } })
}

/** The page that ends with the last message at or before the date. */
export function getMessagesAt(chatId: string, date: string): Promise<MessagePage> {
  return http.get<MessagePage>(`/api/chats/${chatId}/messages/at`, { query: { date, limit: PAGE_SIZE } })
}
