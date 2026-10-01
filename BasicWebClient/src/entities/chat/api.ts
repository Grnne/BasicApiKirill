import { http } from '@/shared/api/http'
import type { ChatDetail, ChatListItem } from './types'

export function getChatItem(chatId: string): Promise<ChatListItem> {
  return http.get<ChatListItem>(`/api/chats/${chatId}/item`)
}

/** The chat with its members (and, for groups, roles and permissions). */
export function getChatDetail(chatId: string): Promise<ChatDetail> {
  return http.get<ChatDetail>(`/api/chats/${chatId}`)
}

/** The user's chat with themselves: created on the first call (201), the same one after (200). */
export function openSavedChat(): Promise<ChatListItem> {
  return http.post<ChatListItem>('/api/chats/saved')
}
