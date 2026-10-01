import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'

import type { AuthResponse } from '@/entities/user/auth.types'
import * as authApi from '@/features/auth/api/auth.api'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { NetworkError } from '@/shared/api/problem'
import LoginPage from './LoginPage.vue'
import { safeRedirect } from './lib/redirect'

vi.mock('@/features/auth/api/auth.api', () => ({ refresh: vi.fn(), login: vi.fn(), register: vi.fn(), logout: vi.fn() }))

const answer: AuthResponse = {
  userId: 'u-1', username: 'anna', email: 'anna@test', displayName: 'Анна', token: 'access',
  expiresAt: '2026-10-01T12:00:00Z', refreshToken: 'token-2', refreshTokenExpiresAt: '2026-11-01T12:00:00Z',
}

beforeEach(() => {
  vi.useFakeTimers()
  localStorage.clear()
  setActivePinia(createPinia())
  vi.mocked(authApi.refresh).mockReset()
})
afterEach(() => vi.useRealTimers())

describe('the login page', () => {
  it('opened because the server was down at start: it signs back in by itself once it is up', async () => {
    // The bug: one failed refresh at page load showed the login form for good, with the session
    // still alive; signing in again left the old one behind.
    localStorage.setItem('basicchat.refreshToken', 'token-1')
    vi.mocked(authApi.refresh).mockRejectedValueOnce(new NetworkError(new TypeError('Failed to fetch')))
    await useAuthStore().restoreSession()
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/login', name: 'login', component: LoginPage },
        { path: '/chat', name: 'chat', component: { template: '<div />' } },
      ],
    })
    await router.push('/login?redirect=/chat')
    const wrapper = mount(LoginPage, { global: { plugins: [router] } })
    expect(wrapper.text()).toContain('Нет связи с сервером')

    vi.mocked(authApi.refresh).mockResolvedValueOnce(answer)
    await vi.advanceTimersByTimeAsync(5_000)
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/chat')
  })
})

describe('where the login leads', () => {
  it('only to a path of this site: an address from the URL could lead elsewhere', () => {
    expect(safeRedirect('/settings')).toBe('/settings')
    expect(safeRedirect('//evil.example')).toBe('/chat')
    expect(safeRedirect('https://evil.example')).toBe('/chat')
    expect(safeRedirect(['/a'])).toBe('/chat')
    expect(safeRedirect(undefined)).toBe('/chat')
  })
})
