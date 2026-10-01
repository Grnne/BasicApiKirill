import { http } from '@/shared/api/http'
import type { SearchUsersResponse, UserProfile } from './types'

/** Matches display name and username; the server excludes the current user. */
export function searchUsers(query: string, signal?: AbortSignal): Promise<SearchUsersResponse> {
  return http.get<SearchUsersResponse>('/api/users/search', {
    query: { q: query, limit: 20 },
    ...(signal ? { signal } : {}),
  })
}

/** Anyone's public profile, by id; 404 for an account that is gone. */
export function getUser(userId: string): Promise<UserProfile> {
  return http.get<UserProfile>(`/api/Users/${userId}`)
}

