// Auth types: generated from the backend contract (shared/api/schema.d.ts).

import type { AuthResponseDto, LoginRequestDto, RegisterRequestDto } from '@/shared/api/schema'

export type LoginRequest = LoginRequestDto
export type RegisterRequest = RegisterRequestDto

/** The same answer for login, register and refresh. The refresh token rotates on every refresh. */
export type AuthResponse = AuthResponseDto
