import { http } from '@/shared/api/http'
import type { GlobalSearchResponseDto } from '@/shared/api/schema'

/** All the user's chats, newest first; at least 2 characters. */
export function searchMessages(query: string, cursor: string | null, signal?: AbortSignal): Promise<GlobalSearchResponseDto> {
  return http.get<GlobalSearchResponseDto>('/api/search/messages', {
    query: { q: query, cursor: cursor ?? undefined, limit: 20 },
    ...(signal ? { signal } : {}),
  })
}
