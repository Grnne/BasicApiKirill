import { http } from '@/shared/api/http'
import type { SearchUsersResponse } from './types'

/** Matches display name and username; the server excludes the current user. */
export function searchUsers(query: string, signal?: AbortSignal): Promise<SearchUsersResponse> {
  return http.get<SearchUsersResponse>('/api/users/search', {
    query: { q: query, limit: 20 },
    ...(signal ? { signal } : {}),
  })
}
