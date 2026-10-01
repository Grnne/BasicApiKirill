import { ref } from 'vue'
import { defineStore } from 'pinia'

import type { JournaledEventName, JournaledEvents } from '@/shared/api/hub.types'
import type { Message } from '../types'
import * as messageApi from '../api'
import {
  addPending,
  applyHistoryEvent,
  emptyHistory,
  messageStored,
  putLatestPage,
  putNewerPage,
  putOlderPage,
  putWindow,
  removePending,
  updatePending,
  type ChatHistory,
  type HistoryContext,
  type PendingMessage,
} from './history'

type PendingChange = Partial<Pick<PendingMessage, 'state' | 'error'>>

/** Message history of the chats opened in this session, kept current by sync. */
export const useHistoryStore = defineStore('history', () => {
  const state = ref(emptyHistory())

  function get(chatId: string | null | undefined): ChatHistory | null {
    return chatId ? state.value.byChat[chatId] ?? null : null
  }

  async function loadLatest(chatId: string, signal?: AbortSignal): Promise<void> {
    const page = await messageApi.getMessagesPage(chatId, null, signal)
    putLatestPage(state.value, chatId, page)
  }

  async function loadOlder(chatId: string): Promise<void> {
    const history = state.value.byChat[chatId]
    if (!history?.hasOlder || !history.olderCursor) return
    const page = await messageApi.getMessagesPage(chatId, history.olderCursor)
    putOlderPage(state.value, chatId, page)
  }

  async function loadContext(chatId: string, messageId: string): Promise<void> {
    putWindow(state.value, chatId, await messageApi.getMessageContext(chatId, messageId))
  }

  async function loadNewer(chatId: string): Promise<void> {
    const history = state.value.byChat[chatId]
    const last = history?.messages.at(-1)
    if (!history?.hasNewer || !last) return
    putNewerPage(state.value, chatId, await messageApi.getMessagesAfter(chatId, last.seq))
  }

  /** newestSeq: the chat's last message, to know whether the page reaches it. */
  async function loadAt(chatId: string, date: string, newestSeq: number): Promise<void> {
    const page = await messageApi.getMessagesAt(chatId, date)
    const last = page.items.at(-1)
    putWindow(state.value, chatId, { ...page, hasNewer: !!last && last.seq < newestSeq })
  }

  function apply<K extends JournaledEventName>(type: K, payload: JournaledEvents[K], ctx: HistoryContext): void {
    applyHistoryEvent(state.value, type, payload, ctx)
  }

  /** After a new snapshot the cached pages may have gaps: drop them; pending sends stay. */
  function invalidate(): void {
    for (const history of Object.values(state.value.byChat)) {
      history.messages = []
      history.olderCursor = null
      history.hasOlder = false
    }
  }

  return {
    get,
    loadLatest,
    loadOlder,
    loadContext,
    loadNewer,
    loadAt,
    apply,
    invalidate,
    stored: (message: Message) => messageStored(state.value, message),
    addPending: (pending: PendingMessage) => addPending(state.value, pending),
    updatePending: (chatId: string, id: string, change: PendingChange) => updatePending(state.value, chatId, id, change),
    removePending: (chatId: string, id: string) => removePending(state.value, chatId, id),
    reset: () => {
      state.value = emptyHistory()
    },
  }
})
