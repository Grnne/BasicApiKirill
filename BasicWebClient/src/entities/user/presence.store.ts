/**
 * Присутствие: кто в сети и кто печатает.
 *
 * Лежит в entities, а не в features, потому что этим пользуются сразу
 * несколько фич — список чатов и окно переписки. Фича не должна лезть
 * в модель другой фичи, а вот в entities можно всем.
 */

import { ref } from 'vue'
import { defineStore } from 'pinia'

import { useHubStore } from '@/shared/api/hub.store'
import * as presenceApi from './presence.api'

/**
 * Сколько считаем человека печатающим, если не пришло явное «перестал».
 * Вкладка собеседника может закрыться посреди набора — без срока
 * «печатает…» осталось бы на экране навсегда.
 */
const TYPING_TTL_MS = 6_000

/** Как часто убираем протухшие отметки. */
const PRUNE_INTERVAL_MS = 2_000

export const usePresenceStore = defineStore('presence', () => {
  const hub = useHubStore()

  /** userId -> в сети. */
  const online = ref<Record<string, boolean>>({})

  /** chatId -> userId -> до какого момента считаем, что он печатает. */
  const typingUntil = ref<Record<string, Record<string, number>>>({})

  function isOnline(userId: string | null | undefined): boolean {
    if (!userId) return false
    return online.value[userId] === true
  }

  /** Печатает ли кто-нибудь в этом чате (кроме нас — себя сервер не присылает). */
  function isSomeoneTyping(chatId: string): boolean {
    const inChat = typingUntil.value[chatId]
    if (!inChat) return false

    const now = Date.now()
    return Object.values(inChat).some((until) => until > now)
  }

  /* ── Загрузка ── */

  async function loadStatuses(userIds: string[]): Promise<void> {
    const unique = [...new Set(userIds.filter(Boolean))]
    if (unique.length === 0) return

    try {
      const response = await presenceApi.getUsersStatus(unique)
      const next = { ...online.value }
      for (const status of response.items) next[status.userId] = status.isOnline
      online.value = next
    } catch {
      // Присутствие — украшение, а не функциональность. Молчим.
    }
  }

  /** Стартовое состояние «печатает»: события, случившиеся до нашего подключения. */
  async function loadTyping(): Promise<void> {
    try {
      const response = await presenceApi.getTypingStatus()
      for (const item of response.items) {
        if (item.isTyping) setTyping(item.chatId, item.userId, true)
      }
    } catch {
      // см. выше
    }
  }

  /* ── События хаба ── */

  function setTyping(chatId: string, userId: string, isTyping: boolean): void {
    const inChat = { ...(typingUntil.value[chatId] ?? {}) }

    if (isTyping) {
      inChat[userId] = Date.now() + TYPING_TTL_MS
    } else {
      delete inChat[userId]
    }

    typingUntil.value = { ...typingUntil.value, [chatId]: inChat }
  }

  /** Убираем отметки, по которым не пришло «перестал печатать». */
  function pruneTyping(): void {
    const now = Date.now()
    const next: Record<string, Record<string, number>> = {}
    let changed = false

    for (const [chatId, inChat] of Object.entries(typingUntil.value)) {
      const alive = Object.entries(inChat).filter(([, until]) => until > now)
      if (alive.length !== Object.keys(inChat).length) changed = true
      if (alive.length > 0) next[chatId] = Object.fromEntries(alive)
    }

    if (changed) typingUntil.value = next
  }

  let isSubscribed = false
  let pruneTimer: ReturnType<typeof setInterval> | undefined

  function subscribeToHub(): void {
    if (isSubscribed) return
    isSubscribed = true

    hub.on('UserOnlineChanged', (userId, isOnlineNow) => {
      online.value = { ...online.value, [userId]: isOnlineNow }
    })

    hub.on('TypingChanged', (chatId, userId, isTyping) => {
      setTyping(chatId, userId, isTyping)
    })

    pruneTimer = setInterval(pruneTyping, PRUNE_INTERVAL_MS)
  }

  function reset(): void {
    clearInterval(pruneTimer)
    pruneTimer = undefined
    isSubscribed = false
    online.value = {}
    typingUntil.value = {}
  }

  return {
    online,
    typingUntil,
    isOnline,
    isSomeoneTyping,
    loadStatuses,
    loadTyping,
    subscribeToHub,
    reset,
  }
})
