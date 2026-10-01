import { http } from '@/shared/api/http'
import type { GroupMemberDto } from '@/shared/api/schema'
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

/** The caller becomes the owner; members get ChatCreated. Answers the caller's row of the group. */
export function createGroup(title: string, memberIds: string[]): Promise<ChatListItem> {
  return http.post<ChatListItem>('/api/chats/groups', { title, memberIds })
}

/** null removes the photo. Members learn of it through ChatUpdated. */
export function setChatAvatar(chatId: string, attachmentId: string | null): Promise<void> {
  return http.put<void>(`/api/chats/${chatId}/avatar`, { attachmentId })
}

/** Needs addMembers; those already in the group are skipped. Answers who was added. */
export function addMembers(chatId: string, userIds: string[]): Promise<GroupMemberDto[]> {
  return http.post<GroupMemberDto[]>(`/api/chats/${chatId}/members`, { userIds })
}

/** Removes a member; the caller's own id — leaves the group. */
export function removeMember(chatId: string, userId: string): Promise<void> {
  return http.delete<void>(`/api/chats/${chatId}/members/${userId}`)
}
