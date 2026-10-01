import { http } from '@/shared/api/http'
import type { MessagePage } from '@/entities/message/types'

/** The server caps the page size at 100. */
export const PAGE_SIZE = 30

/**
 * Items within a page go oldest to newest, but the cursor walks back in time: the next page is
 * older than the current one. Without a cursor the newest page is returned.
 */
export function getMessagesPage(
  chatId: string,
  cursor: string | null,
  signal?: AbortSignal,
): Promise<MessagePage> {
  return http.get<MessagePage>(`/api/chats/${chatId}/messages/cursor`, {
    query: { limit: PAGE_SIZE, cursor: cursor ?? undefined },
    ...(signal ? { signal } : {}),
  })
}

/** Marks everything up to and including the given message as read. */
export function markRead(chatId: string, lastMessageId: string): Promise<void> {
  return http.post<void>(`/api/chats/${chatId}/read`, { lastMessageId })
}
