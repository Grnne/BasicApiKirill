import type { ChatListItem } from './types'

/** A private chat is titled by the companion's name, a group chat by its own title. */
export function chatTitle(chat: ChatListItem): string {
  // The server leaves the title of "Saved messages" to the client.
  if (chat.type === 'saved') return 'Избранное'
  if (chat.type === 'private') {
    return chat.companionName || chat.companionUsername || 'Без имени'
  }
  return chat.title || 'Без названия'
}

export function chatInitial(chat: ChatListItem): string {
  if (chat.type === 'saved') return '★'
  return chatTitle(chat).trim().charAt(0).toUpperCase() || '?'
}

/** Muted now: forever (no until) or until a moment still ahead. */
export function isMutedNow(chat: ChatListItem, now = Date.now()): boolean {
  if (!chat.isMuted) return false
  return chat.mutedUntil === null || Date.parse(chat.mutedUntil) > now
}
