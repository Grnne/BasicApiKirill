import { http } from '@/shared/api/http'
import type { ChatDetail, ChatListItem } from './types'

export function getChatItem(chatId: string): Promise<ChatListItem> {
  return http.get<ChatListItem>(`/api/chats/${chatId}/item`)
}

/** The chat with its members (and, for groups, roles and permissions). */
export function getChatDetail(chatId: string): Promise<ChatDetail> {
  return http.get<ChatDetail>(`/api/chats/${chatId}`)
}
