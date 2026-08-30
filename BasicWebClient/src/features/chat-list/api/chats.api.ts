/* Запросы по чатам. BasicApi/Features/Chats/ChatsController.cs */

import { http } from '@/shared/api/http'
import type { ChatListItem, SearchChatsResponse } from '@/entities/chat/types'

/** Все чаты текущего пользователя, уже с превью последнего сообщения. */
export function getChats(): Promise<ChatListItem[]> {
  return http.get<ChatListItem[]>('/api/chats')
}

/**
 * Одна строка списка. Нужна, когда пришло событие про чат, которого у нас нет
 * (например, вкладка была закрыта в момент его создания) — дешевле, чем
 * перезагружать весь список.
 */
export function getChatItem(chatId: string): Promise<ChatListItem> {
  return http.get<ChatListItem>(`/api/chats/${chatId}/item`)
}

/**
 * Создать приватный чат или получить существующий (сервер вернёт 200 вместо
 * 201, если он уже есть). В обоих случаях тело — готовая строка списка.
 */
export function createPrivateChat(userId: string): Promise<ChatListItem> {
  return http.post<ChatListItem>(`/api/chats/private/${userId}`)
}

export function searchChats(query: string, signal?: AbortSignal): Promise<SearchChatsResponse> {
  return http.get<SearchChatsResponse>('/api/chats/search', {
    query: { q: query, limit: 20 },
    ...(signal ? { signal } : {}),
  })
}
