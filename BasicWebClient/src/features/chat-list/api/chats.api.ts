import { http } from '@/shared/api/http'
import type { ChatListItem, SearchChatsResponse } from '@/entities/chat/types'

export function getChats(): Promise<ChatListItem[]> {
  return http.get<ChatListItem[]>('/api/chats')
}

/** For an event about a chat missing from the list; cheaper than reloading the whole list. */
export function getChatItem(chatId: string): Promise<ChatListItem> {
  return http.get<ChatListItem>(`/api/chats/${chatId}/item`)
}

/** Idempotent: returns the existing chat (200) or creates one (201), both as a list item. */
export function createPrivateChat(userId: string): Promise<ChatListItem> {
  return http.post<ChatListItem>(`/api/chats/private/${userId}`)
}

export function searchChats(query: string, signal?: AbortSignal): Promise<SearchChatsResponse> {
  return http.get<SearchChatsResponse>('/api/chats/search', {
    query: { q: query, limit: 20 },
    ...(signal ? { signal } : {}),
  })
}
