/* Типы авторизации. По BasicApi/Models/Dto/Auth. */

export interface LoginRequest {
  usernameOrEmail: string
  password: string
}

export interface RegisterRequest {
  username: string
  email: string
  password: string
  displayName?: string
}

/** Ответ login / register / refresh — одинаковый. */
export interface AuthResponse {
  userId: string
  username: string
  email: string
  displayName: string
  /** Короткоживущий JWT. Держим только в памяти. */
  token: string
  expiresAt: string
  /** Долгоживущий токен. Ротируется на каждом refresh — всегда сохраняем новый. */
  refreshToken: string
  refreshTokenExpiresAt: string
}
