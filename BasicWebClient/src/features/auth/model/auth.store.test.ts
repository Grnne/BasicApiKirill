import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { AuthResponse } from '@/entities/user/auth.types'
import { ApiError } from '@/shared/api/problem'
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

  it('another user signed in from another tab: this tab ends its session instead of taking theirs', async () => {
    // The bug: a tab that slept through a logout and someone else's login refreshed with the new
    // token and went on showing the first user's chats under the second user's name.
    localStorage.setItem(KEY, 'token-1')
    const auth = useAuthStore()
    vi.mocked(authApi.refresh).mockResolvedValueOnce(answer('token-2'))
    await auth.restoreSession()
    localStorage.setItem(KEY, 'token-of-bob')
    vi.mocked(authApi.refresh).mockResolvedValueOnce({ ...answer('token-bob-2'), userId: 'u-bob', username: 'bob' })

    expect(await auth.refreshTokens()).toBe(false)

    expect(auth.isAuthenticated).toBe(false)
    expect(auth.sessionLost).toBe(true)
    // The other tab's sign-in is left alone.
    expect(localStorage.getItem(KEY)).toBe('token-bob-2')
  })

  it('a logout in another tab ends the session here too', async () => {
    localStorage.setItem(KEY, 'token-1')
    const auth = useAuthStore()
    localStorage.removeItem(KEY)

    expect(await auth.refreshTokens()).toBe(false)
    expect(authApi.refresh).not.toHaveBeenCalled()
  })
})

describe('a session that ends by itself', () => {
  it('signed out from another device: the refresh is refused and the session counts as lost', async () => {
    localStorage.setItem(KEY, 'token-1')
    const auth = useAuthStore()
    await auth.restoreSession().catch(() => {})
    vi.mocked(authApi.refresh).mockRejectedValue(new ApiError(401, { errorCode: 'SESSION_REVOKED' }))

    expect(await auth.refreshTokens()).toBe(false)

    expect(auth.isAuthenticated).toBe(false)
    expect(auth.sessionLost).toBe(true)
  })

  it('a logout names the refresh token another tab rotated, not the one this tab started with', async () => {
    vi.mocked(authApi.logout).mockResolvedValue(undefined)
    localStorage.setItem(KEY, 'token-1')
    const auth = useAuthStore()
    localStorage.setItem(KEY, 'token-2')

    await auth.logout()

    expect(authApi.logout).toHaveBeenCalledWith('token-2')
  })

  it('"log out everywhere" ends this sign-in too: its refusals during the sign-out are not a lost session', async () => {
    // The server stops taking this tab's token at once: the hub reconnect and the clean-up
    // requests get 401, and the refresh after them is refused.
    vi.mocked(authApi.refresh).mockResolvedValueOnce(answer('token-2'))
    localStorage.setItem(KEY, 'token-1')
    const auth = useAuthStore()
    await auth.refreshTokens()
    vi.mocked(authApi.refresh).mockRejectedValue(new ApiError(401, { errorCode: 'SESSION_REVOKED' }))

    await auth.whileSigningOut(async () => {
      expect(await auth.refreshTokens()).toBe(false)
    })

    expect(auth.isAuthenticated).toBe(false)
    expect(auth.sessionLost).toBe(false)
  })

  it('an own logout is not a lost session', async () => {
    vi.mocked(authApi.refresh).mockResolvedValue(answer('token-2'))
    vi.mocked(authApi.logout).mockResolvedValue(undefined)
    localStorage.setItem(KEY, 'token-1')
    const auth = useAuthStore()
    await auth.refreshTokens()

    await auth.logout()

    expect(auth.isAuthenticated).toBe(false)
    expect(auth.sessionLost).toBe(false)
  })
})
