import { http } from '@/shared/api/http'
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
