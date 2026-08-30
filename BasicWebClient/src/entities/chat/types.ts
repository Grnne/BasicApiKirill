/* Типы чатов. По BasicApi/Models/Dto/Chat. */

import type { Message } from '@/entities/message/types'

export type ChatType = 'private' | 'group'

/**
 * Строка списка чатов. Companion-поля описывают собеседника
 * с точки зрения текущего пользователя и заполнены только для приватных чатов.
 */
export interface ChatListItem {
  chatId: string
  type: string
  title: string | null
  companionId: string | null
  companionName: string | null
  companionUsername: string | null
  lastMessage: Message | null
  unreadCount: number
  /** ISO-строка. Разбирать только через parseApiDate. */
  lastActivityAt: string
}

export interface ChatParticipant {
  userId: string
  displayName: string
  username: string
}

/** Детали чата со списком участников. GET /api/chats/{id} */
export interface ChatDetail {
  chatId: string
  type: string
  title: string | null
  participants: ChatParticipant[]
}

export interface SearchChatsResponse {
  items: ChatListItem[]
  query: string
  totalCount: number
}
