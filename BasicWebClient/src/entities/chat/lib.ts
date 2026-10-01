import type { ChatListItem } from './types'

/** A private chat is titled by the companion's name, a group chat by its own title. */
export function chatTitle(chat: ChatListItem): string {
  if (chat.type === 'private') {
    return chat.companionName || chat.companionUsername || 'Без имени'
  }
  return chat.title || 'Без названия'
}

export function chatInitial(chat: ChatListItem): string {
  return chatTitle(chat).trim().charAt(0).toUpperCase() || '?'
}
