import { http } from '@/shared/api/http'
/** Marks everything up to and including the given message as read. */
export function markRead(chatId: string, lastMessageId: string): Promise<void> {
  return http.post<void>(`/api/chats/${chatId}/read`, { lastMessageId })
}
