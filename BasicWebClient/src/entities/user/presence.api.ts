import { http } from '@/shared/api/http'
import type { TypingStatusResponse, UserStatusResponse } from './types'

/** Server-side limit on ids per request. */
export const MAX_STATUS_IDS = 200

/**
 * The response includes offline users too; users without a shared chat are silently dropped
 * by the server.
 */
export function getUsersStatus(userIds: string[]): Promise<UserStatusResponse> {
  return http.post<UserStatusResponse>('/api/users/status', {
    userIds: userIds.slice(0, MAX_STATUS_IDS),
  })
}

/** Who is typing across all of the user's chats. */
export function getTypingStatus(): Promise<TypingStatusResponse> {
  return http.get<TypingStatusResponse>('/api/users/typing')
}
