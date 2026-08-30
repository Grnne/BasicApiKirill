/* Запросы авторизации. BasicApi/Features/Auth/AuthController.cs */

import { http } from '@/shared/api/http'
import type { AuthResponse, LoginRequest, RegisterRequest } from '@/entities/user/auth.types'

/**
 * auth: false у всех запросов ниже — они не должны получать заголовок
 * Authorization и, главное, не должны запускать повторный refresh при 401.
 * Иначе неверный пароль на логине уводил бы клиент в попытку обновить токен.
 */

export function login(request: LoginRequest): Promise<AuthResponse> {
  return http.post<AuthResponse>('/api/auth/login', request, { auth: false })
}

export function register(request: RegisterRequest): Promise<AuthResponse> {
  return http.post<AuthResponse>('/api/auth/register', request, { auth: false })
}

/** Обмен refresh-токена на новую пару. Старый после этого недействителен. */
export function refresh(refreshToken: string): Promise<AuthResponse> {
  return http.post<AuthResponse>('/api/auth/refresh', { refreshToken }, { auth: false })
}

/**
 * Завершить текущую сессию. Refresh-токен обязателен: именно его отзывает
 * сервер. Без него сессия останется живой до истечения срока.
 */
export function logout(refreshToken: string | null): Promise<void> {
  return http.post<void>('/api/auth/logout', { refreshToken })
}

/** Завершить сессии на всех устройствах. */
export function logoutAll(): Promise<void> {
  return http.post<void>('/api/auth/logout-all')
}
