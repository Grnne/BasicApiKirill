/* Поиск пользователей. BasicApi/Features/Users/UsersController.cs */

import { http } from '@/shared/api/http'
import type { SearchUsersResponse } from '@/entities/user/types'

/** Ищет по имени и логину. Текущего пользователя сервер из выдачи убирает. */
export function searchUsers(query: string, signal?: AbortSignal): Promise<SearchUsersResponse> {
  return http.get<SearchUsersResponse>('/api/users/search', {
    query: { q: query, limit: 20 },
    ...(signal ? { signal } : {}),
  })
}
