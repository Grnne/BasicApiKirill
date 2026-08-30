/* Сообщения чата. BasicApi/Features/Chats/ChatsController.cs */

import { http } from '@/shared/api/http'
import type { CursorPage, Message } from '@/entities/message/types'

/** Сколько сообщений тянем за раз. Сервер ограничивает сотней. */
export const PAGE_SIZE = 30

/**
 * Страница сообщений. Внутри страницы они идут от старых к новым, а сама
 * следующая страница — старее текущей: cursor ведёт назад по времени.
 * Без cursor отдаются самые свежие.
 */
export function getMessagesPage(
  chatId: string,
  cursor: string | null,
  signal?: AbortSignal,
): Promise<CursorPage<Message>> {
  return http.get<CursorPage<Message>>(`/api/chats/${chatId}/messages/cursor`, {
    query: { limit: PAGE_SIZE, cursor: cursor ?? undefined },
    ...(signal ? { signal } : {}),
  })
}

/** Отметить прочитанным всё до указанного сообщения включительно. */
export function markRead(chatId: string, lastMessageId: string): Promise<void> {
  return http.post<void>(`/api/chats/${chatId}/read`, { lastMessageId })
}
