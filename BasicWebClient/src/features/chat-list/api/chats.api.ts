import { http } from '@/shared/api/http'
import type { ChatListItem, SearchChatsResponse } from '@/entities/chat/types'
import type { ChatStateDto, PinnedChatsDto } from '@/shared/api/schema'

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

/** The "unread" mark is only the user's; reading the chat clears it. */
export function setMarkedUnread(chatId: string, markedUnread: boolean): Promise<void> {
  return http.put<void>(`/api/chats/${chatId}/marked-unread`, { markedUnread })
}

/** Reads everything up to the message (and clears the "unread" mark). */
export function markRead(chatId: string, lastMessageId: string): Promise<void> {
  return http.post<void>(`/api/chats/${chatId}/read`, { lastMessageId })
}

/** A new pin goes on top (at most 10); pinning takes a chat out of the archive. */
export function setPinned(chatId: string, pinned: boolean): Promise<PinnedChatsDto> {
  return http.put<PinnedChatsDto>(`/api/chats/${chatId}/pinned`, { pinned })
}

/** Exactly the pinned chats, in the new order. */
export function reorderPinned(chatIds: string[]): Promise<void> {
  return http.put<void>('/api/chats/pinned', { chatIds })
}

/** Archiving unpins. */
export function setArchived(chatId: string, archived: boolean): Promise<ChatStateDto> {
  return http.put<ChatStateDto>(`/api/chats/${chatId}/archived`, { archived })
}

/** Without until — forever. */
export function setMuted(chatId: string, muted: boolean, until?: string): Promise<ChatStateDto> {
  return http.put<ChatStateDto>(`/api/chats/${chatId}/muted`, until ? { muted, until } : { muted })
}
