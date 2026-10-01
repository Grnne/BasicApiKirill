import { http } from '@/shared/api/http'
import type { AuthResponse, LoginRequest, RegisterRequest } from '@/entities/user/auth.types'

/*
 * login/register/refresh use auth: false so a 401 never triggers a token refresh: a wrong
 * password must not send the client into a refresh attempt.
 */

export function login(request: LoginRequest): Promise<AuthResponse> {
  return http.post<AuthResponse>('/api/auth/login', request, { auth: false })
}

export function register(request: RegisterRequest): Promise<AuthResponse> {
  return http.post<AuthResponse>('/api/auth/register', request, { auth: false })
}

/** Rotates the pair: the old refresh token is invalid afterwards. */
export function refresh(refreshToken: string): Promise<AuthResponse> {
  return http.post<AuthResponse>('/api/auth/refresh', { refreshToken }, { auth: false })
}

/**
 * The server revokes the session by its refresh token; without it the session stays alive until
 * it expires.
 */
export function logout(refreshToken: string | null): Promise<void> {
  return http.post<void>('/api/auth/logout', { refreshToken })
}

/** Ends the sessions on all devices. */
export function logoutAll(): Promise<void> {
  return http.post<void>('/api/auth/logout-all')
}
