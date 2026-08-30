/* Онлайн-статусы и «печатает». BasicApi/Features/Users/UsersController.cs */

import { http } from '@/shared/api/http'
import type { TypingStatusResponse, UserStatusResponse } from './types'

/** Сервер принимает не больше 200 id за раз. */
export const MAX_STATUS_IDS = 200

/**
 * Статусы явного списка пользователей: в ответе есть и офлайновые.
 * Тех, с кем нет общего чата, сервер молча выкидывает из ответа.
 */
export function getUsersStatus(userIds: string[]): Promise<UserStatusResponse> {
  return http.post<UserStatusResponse>('/api/users/status', {
    userIds: userIds.slice(0, MAX_STATUS_IDS),
  })
}

/** Кто сейчас печатает — по всем чатам пользователя. */
export function getTypingStatus(): Promise<TypingStatusResponse> {
  return http.get<TypingStatusResponse>('/api/users/typing')
}
