/**
 * Состояние авторизации.
 *
 * Где живут токены:
 *  - access — только в памяти. Он короткоживущий, и при перезагрузке
 *    страницы восстанавливается из refresh-токена;
 *  - refresh — в localStorage, иначе каждый F5 требовал бы пароль.
 *
 * Плата за localStorage — XSS: скрипт на странице сможет прочитать токен.
 * Поэтому в приложении нет ни одного v-html, а токен ротируется на каждом
 * обновлении (сервер отзывает всю цепочку сессий, если старый токен
 * попробуют использовать повторно).
 */

import { computed, ref, shallowRef } from 'vue'
import { defineStore } from 'pinia'

import type { AuthResponse, LoginRequest, RegisterRequest } from '@/entities/user/auth.types'
import type { OwnProfile } from '@/entities/user/types'
import { ApiError } from '@/shared/api/problem'
import { readLocal, removeLocal, writeLocal } from '@/shared/lib/storage'
import * as authApi from '../api/auth.api'

const REFRESH_TOKEN_KEY = 'basicchat.refreshToken'

export const useAuthStore = defineStore('auth', () => {
  /** Access-токен. Намеренно не сохраняется никуда. */
  const accessToken = ref<string | null>(null)
  const refreshToken = ref<string | null>(readLocal(REFRESH_TOKEN_KEY))
  const user = shallowRef<OwnProfile | null>(null)

  /** false, пока не отработала попытка восстановить сессию при старте. */
  const isSessionRestored = ref(false)

  const isAuthenticated = computed(() => accessToken.value !== null && user.value !== null)

  /* ── Внутреннее ── */

  function applyAuth(response: AuthResponse): void {
    accessToken.value = response.token
    refreshToken.value = response.refreshToken
    user.value = {
      userId: response.userId,
      username: response.username,
      email: response.email,
      displayName: response.displayName,
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
   * Общий Promise на все параллельные обновления токена.
   *
   * Без него три запроса, получившие 401 одновременно, отправили бы три
   * refresh'а с одним и тем же токеном. Сервер прощает повтор в течение 30
   * секунд, но за этим окном считает это кражей токена и убивает все сессии
   * пользователя. Проще не создавать гонку, чем полагаться на снисходительность.
   */
  const pendingRefresh = shallowRef<Promise<boolean> | null>(null)

  async function performRefresh(): Promise<boolean> {
    const token = refreshToken.value
    if (!token) return false

    try {
      applyAuth(await authApi.refresh(token))
      return true
    } catch (error) {
      // 401 — токен мёртв (истёк, отозван, уже использован): нужен пароль.
      // Всё остальное (сеть, 500) — временное, сессию не трогаем.
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

  /* ── Публичные действия ── */

  async function login(request: LoginRequest): Promise<void> {
    applyAuth(await authApi.login(request))
  }

  async function register(request: RegisterRequest): Promise<void> {
    applyAuth(await authApi.register(request))
  }

  /**
   * Восстановление сессии при загрузке страницы: access-токена в памяти нет,
   * но refresh мог сохраниться с прошлого раза. Ответ refresh'а содержит и
   * данные пользователя, так что отдельный запрос за профилем не нужен.
   */
  async function restoreSession(): Promise<void> {
    if (isSessionRestored.value) return
    if (refreshToken.value) await refreshTokens()
    isSessionRestored.value = true
  }

  async function logout(): Promise<void> {
    const token = refreshToken.value
    try {
      // Даже если запрос не дойдёт — локально разлогиниваемся в любом случае.
      await authApi.logout(token)
    } catch {
      // Сервер отзовёт сессию по таймауту; молчим, чтобы не блокировать выход.
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
