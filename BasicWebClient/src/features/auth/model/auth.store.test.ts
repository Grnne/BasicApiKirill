import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { AuthResponse } from '@/entities/user/auth.types'
import * as authApi from '../api/auth.api'
import { useAuthStore } from './auth.store'

vi.mock('../api/auth.api', () => ({ refresh: vi.fn(), login: vi.fn(), register: vi.fn(), logout: vi.fn() }))

const KEY = 'basicchat.refreshToken'

const answer = (refreshToken: string): AuthResponse => ({
  userId: 'u-1',
  username: 'anna',
  email: 'anna@test',
  displayName: 'Анна',
  token: 'access',
  expiresAt: '2026-10-01T12:00:00Z',
  refreshToken,
  refreshTokenExpiresAt: '2026-11-01T12:00:00Z',
})

beforeEach(() => {
  localStorage.clear()
  setActivePinia(createPinia())
  vi.mocked(authApi.refresh).mockReset()
})

describe('refresh across tabs', () => {
  it('uses the refresh token another tab rotated, not the one this tab started with', async () => {
    // The bug: each tab kept the token it read at start; after the other tab rotated it, this
    // tab presented a used token and the server revoked the whole sign-in as a theft.
    localStorage.setItem(KEY, 'token-1')
    const auth = useAuthStore()
    localStorage.setItem(KEY, 'token-2')
    vi.mocked(authApi.refresh).mockResolvedValue(answer('token-3'))

    expect(await auth.refreshTokens()).toBe(true)

    expect(authApi.refresh).toHaveBeenCalledWith('token-2')
    expect(localStorage.getItem(KEY)).toBe('token-3')
  })

  it('a logout in another tab ends the session here too', async () => {
    localStorage.setItem(KEY, 'token-1')
    const auth = useAuthStore()
    localStorage.removeItem(KEY)

    expect(await auth.refreshTokens()).toBe(false)
    expect(authApi.refresh).not.toHaveBeenCalled()
  })
})
