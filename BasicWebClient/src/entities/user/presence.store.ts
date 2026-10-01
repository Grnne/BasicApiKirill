/* Online and typing state; in entities because both the chat list and the chat window use it. */

import { ref, watch } from 'vue'
import { defineStore } from 'pinia'

import { useHubStore } from '@/shared/api/hub.store'
import * as presenceApi from './presence.api'

/**
 * Typing expires without an explicit "stopped": the other tab may close mid-typing, and the
 * indicator would otherwise stay forever.
 */
const TYPING_TTL_MS = 6_000

const PRUNE_INTERVAL_MS = 2_000

export const usePresenceStore = defineStore('presence', () => {
  const hub = useHubStore()

  /** userId -> is online. */
  const online = ref<Record<string, boolean>>({})

  /** chatId -> userId -> typing expiry timestamp (ms). */
  const typingUntil = ref<Record<string, Record<string, number>>>({})

  function isOnline(userId: string | null | undefined): boolean {
    if (!userId) return false
    return online.value[userId] === true
  }

  /** The server never reports our own typing, so this means someone else. */
  function isSomeoneTyping(chatId: string): boolean {
    const inChat = typingUntil.value[chatId]
    if (!inChat) return false

    const now = Date.now()
    return Object.values(inChat).some((until) => until > now)
  }

  async function loadStatuses(userIds: string[]): Promise<void> {
    const unique = [...new Set(userIds.filter(Boolean))]
    if (unique.length === 0) return

    try {
      const response = await presenceApi.getUsersStatus(unique)
      const next = { ...online.value }
      for (const status of response.items) next[status.userId] = status.isOnline
      online.value = next
    } catch {
      // Presence is cosmetic; failures are ignored.
    }
  }

  /** Statuses asked for and not answered yet: a chat list that changes asks only once. */
  const asking = new Set<string>()

  /** Users whose status is shown (companions of chats that appear later): asked once each. */
  async function track(userIds: string[]): Promise<void> {
    const unknown = [...new Set(userIds)].filter((id) => id && !(id in online.value) && !asking.has(id))
    if (unknown.length === 0) return
    unknown.forEach((id) => asking.add(id))
    try {
      await loadStatuses(unknown)
    } finally {
      unknown.forEach((id) => asking.delete(id))
    }
  }

  /** Initial typing state: events that happened before we connected. */
  async function loadTyping(): Promise<void> {
    try {
      const response = await presenceApi.getTypingStatus()
      for (const item of response.items) {
        if (item.isTyping) setTyping(item.chatId, item.userId, true)
      }
    } catch {
      // Presence is cosmetic; failures are ignored.
    }
  }

  function setTyping(chatId: string, userId: string, isTyping: boolean): void {
    const inChat = { ...(typingUntil.value[chatId] ?? {}) }

    if (isTyping) {
      inChat[userId] = Date.now() + TYPING_TTL_MS
    } else {
      delete inChat[userId]
    }

    typingUntil.value = { ...typingUntil.value, [chatId]: inChat }
  }

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

  let unsubscribe: (() => void)[] = []
  let pruneTimer: ReturnType<typeof setInterval> | undefined

  function subscribeToHub(): void {
    if (unsubscribe.length > 0) return

    // Online changes and typing are not journaled: while the connection was down they were
    // missed, so the shown statuses are asked again when it is back.
    let wasDown = false
    const stopWatch = watch(
      () => hub.status,
      (status, before) => {
        if (status !== 'connected') {
          // The first connection is not a drop: the snapshot asks for the statuses then.
          if (before === 'connected') wasDown = true
          return
        }
        if (!wasDown) return
        wasDown = false
        void loadStatuses(Object.keys(online.value))
        void loadTyping()
      },
    )

    unsubscribe = [
      stopWatch,
      hub.on('UserOnlineChanged', (userId, isOnlineNow) => {
        online.value = { ...online.value, [userId]: isOnlineNow }
      }),
      hub.on('TypingChanged', (chatId, userId, isTyping) => {
        setTyping(chatId, userId, isTyping)
      }),
    ]

    pruneTimer = setInterval(pruneTyping, PRUNE_INTERVAL_MS)
  }

  function reset(): void {
    clearInterval(pruneTimer)
    pruneTimer = undefined
    for (const off of unsubscribe) off()
    unsubscribe = []
    online.value = {}
    typingUntil.value = {}
    asking.clear()
  }

  return {
    online,
    typingUntil,
    isOnline,
    isSomeoneTyping,
    loadStatuses,
    track,
    loadTyping,
    subscribeToHub,
    reset,
  }
})
