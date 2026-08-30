/**
 * События хаба. Список — по BasicApi/Hubs/ChatHub.cs.
 *
 * Здесь описаны ровно те аргументы, что реально шлёт сервер. Это важнее, чем
 * кажется: старый клиент подписывался на ChatCreated как на (chatId, dto),
 * а сервер шлёт ОДИН аргумент — готовый элемент списка чатов. Такое
 * несовпадение TypeScript поймать не может, поэтому сверяемся с хабом руками.
 */

// Единственное место, где shared смотрит на entities: события хаба — это
// контракт сервера, и он выражен теми же DTO, что и REST. Дублировать их
// здесь было бы хуже, чем один осознанный импорт.
import type { ChatListItem } from '@/entities/chat/types'
import type { Message } from '@/entities/message/types'

export interface HubEvents {
  /** Новое сообщение в чате, куда мы вошли через JoinChat. */
  MessageCreated: (message: Message) => void

  /** Превью последнего сообщения для списка чатов. Приходит всем участникам. */
  ChatListUpdated: (chatId: string, message: Message) => void

  /** Нас добавили в новый чат. Payload — готовая строка списка, собранная для нас. */
  ChatCreated: (chat: ChatListItem) => void

  UserOnlineChanged: (userId: string, isOnline: boolean) => void

  TypingChanged: (chatId: string, userId: string, isTyping: boolean) => void
}

export type HubEventName = keyof HubEvents

/** Имена нужны и в рантайме — по ним подписываемся на соединение. */
export const HUB_EVENT_NAMES: HubEventName[] = [
  'MessageCreated',
  'ChatListUpdated',
  'ChatCreated',
  'UserOnlineChanged',
  'TypingChanged',
]

export type ConnectionStatus = 'disconnected' | 'connecting' | 'connected' | 'reconnecting'
