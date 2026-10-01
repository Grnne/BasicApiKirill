/*
 * Token storage: the access token lives only in memory and is restored from the refresh token on
 * reload; the refresh token is in localStorage so a reload does not ask for the password.
 * The price is XSS exposure, hence: no v-html anywhere, and the refresh token rotates on every
 * refresh (the server revokes the whole session chain if an old token is reused).
 */

import { computed, ref, shallowRef } from 'vue'
import { defineStore } from 'pinia'

import type { AuthResponse, LoginRequest, RegisterRequest } from '@/entities/user/auth.types'
import type { OwnProfile } from '@/entities/user/types'
import { ApiError } from '@/shared/api/problem'
import { isStorageAvailable, readLocal, removeLocal, writeLocal } from '@/shared/lib/storage'
import * as authApi from '../api/auth.api'

const REFRESH_TOKEN_KEY = 'basicchat.refreshToken'

export const useAuthStore = defineStore('auth', () => {
  /** Deliberately never persisted. */
  const accessToken = ref<string | null>(null)
  const refreshToken = ref<string | null>(readLocal(REFRESH_TOKEN_KEY))
  const user = shallowRef<OwnProfile | null>(null)

  /** False until the startup restore attempt has finished, whatever its outcome. */
  const isSessionRestored = ref(false)

  const isAuthenticated = computed(() => accessToken.value !== null && user.value !== null)

  function applyAuth(response: AuthResponse): void {
    accessToken.value = response.token
    refreshToken.value = response.refreshToken
    user.value = {
      userId: response.userId,
      username: response.username,
      email: response.email,
      displayName: response.displayName,
      // The auth answer has no avatar; the profile comes in full with the sync snapshot.
      avatarId: user.value?.userId === response.userId ? user.value.avatarId : null,
    }
    writeLocal(REFRESH_TOKEN_KEY, response.refreshToken)
  }

  function clearSession(): void {
    accessToken.value = null
    refreshToken.value = null
    user.value = null
    removeLocal(REFRESH_TOKEN_KEY)
  }

  /**
   * One shared refresh for all concurrent 401s. Otherwise each would send the same refresh token;
   * the server tolerates a reuse only within a 30 s grace window and past it treats it as token
   * theft and revokes all of the user's sessions.
   */
  const pendingRefresh = shallowRef<Promise<boolean> | null>(null)

  /**
   * Tabs of the app share the refresh token through localStorage, and every refresh rotates it:
   * one tab must not present a token another tab already used (the server would revoke the whole
   * sign-in). So the token is read fresh, under a lock that puts the tabs' refreshes in a row.
   */
  function withRefreshLock<T>(run: () => Promise<T>): Promise<T> {
    const locks = typeof navigator !== 'undefined' ? navigator.locks : undefined
    return locks ? locks.request('basicchat.refresh', run) : run()
  }

  async function performRefresh(): Promise<boolean> {
    return withRefreshLock(refreshOnce)
  }

  async function refreshOnce(): Promise<boolean> {
    const token = isStorageAvailable() ? readLocal(REFRESH_TOKEN_KEY) : refreshToken.value
    if (!token) {
      // Logged out in another tab.
      if (refreshToken.value) clearSession()
      return false
    }

    try {
      applyAuth(await authApi.refresh(token))
      return true
    } catch (error) {
      // Only a 401 means the token is dead; network errors and 5xx keep the session.
      if (error instanceof ApiError && error.isUnauthorized) clearSession()
      return false
    }
  }

  function refreshTokens(): Promise<boolean> {
    if (pendingRefresh.value) return pendingRefresh.value

    const attempt = performRefresh().finally(() => {
      pendingRefresh.value = null
    })
    pendingRefresh.value = attempt
    return attempt
  }

  async function login(request: LoginRequest): Promise<void> {
    applyAuth(await authApi.login(request))
  }

  async function register(request: RegisterRequest): Promise<void> {
    applyAuth(await authApi.register(request))
  }

  /** The refresh response carries the user too, so no separate profile request is needed. */
  async function restoreSession(): Promise<void> {
    if (isSessionRestored.value) return
    if (refreshToken.value) await refreshTokens()
    isSessionRestored.value = true
  }

  async function logout(): Promise<void> {
    const token = refreshToken.value
    try {
      await authApi.logout(token)
    } catch {
      // A failed request must not block logging out locally; the session expires on the server.
    } finally {
      clearSession()
    }
  }

  return {
    accessToken,
    user,
    isAuthenticated,
    isSessionRestored,
    login,
    register,
    logout,
    refreshTokens,
    restoreSession,
  }
})
