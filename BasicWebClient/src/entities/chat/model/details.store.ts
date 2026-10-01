import { ref } from 'vue'
import { defineStore } from 'pinia'

import type { JournaledEventName, JournaledEvents } from '@/shared/api/hub.types'
import type { ChatDetail, ChatParticipant } from '../types'
import * as chatApi from '../api'
import { DETAIL_EVENTS, applyDetailsEvent, type DetailsState } from './details'

const RETRY_AFTER_MS = 30_000

/** Chat details (members, roles) fetched on demand and kept current by sync events. */
export const useChatDetailsStore = defineStore('chatDetails', () => {
  const state = ref<DetailsState>({})
  const inFlight = new Map<string, Promise<void>>()
  /** chatId -> when its load failed: renders do not retry it in a loop. */
  const failedAt = new Map<string, number>()
  /** Loads an event came during: their answer may predate it, so they run once more. */
  const stale = new Set<string>()
  /** Grows on invalidate: an answer requested before it may be stale. */
  let generation = 0

  function load(chatId: string): Promise<void> {
    const running = inFlight.get(chatId)
    if (running) return running

    const asked = generation
    const request = chatApi
      .getChatDetail(chatId)
      .then((detail) => {
        if (asked === generation) state.value[chatId] = detail
      })
      .catch(() => {
        failedAt.set(chatId, Date.now())
      })
      .finally(() => {
        if (inFlight.get(chatId) !== request) return
        inFlight.delete(chatId)
        if (stale.delete(chatId)) void load(chatId)
      })
    inFlight.set(chatId, request)
    return request
  }

  /** The details if loaded; otherwise they are fetched and appear reactively. */
  function get(chatId: string): ChatDetail | null {
    const detail = state.value[chatId]
    if (!detail && Date.now() - (failedAt.get(chatId) ?? 0) > RETRY_AFTER_MS) void load(chatId)
    return detail ?? null
  }

  function member(chatId: string, userId: string): ChatParticipant | null {
    return get(chatId)?.participants.find((p) => p.userId === userId) ?? null
  }

  function apply<K extends JournaledEventName>(type: K, payload: JournaledEvents[K], meId: string): void {
    if (!DETAIL_EVENTS.has(type)) return
    for (const chatId of inFlight.keys()) stale.add(chatId)
    applyDetailsEvent(state.value, type, payload, meId)
  }

  /** After a new snapshot events may have been missed: what is needed is fetched again. */
  function invalidate(): void {
    generation += 1
    inFlight.clear()
    failedAt.clear()
    stale.clear()
    state.value = {}
  }

  return { get, member, load, apply, invalidate, reset: invalidate }
})
