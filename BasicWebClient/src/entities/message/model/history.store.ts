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
  openHistory,
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

  /**
   * Chats whose newest page is on its way. The server may have built the page before an edit,
   * a deletion or a reaction that arrives meanwhile: the page is asked for once more then.
   */
  const loadingLatest = new Map<string, { stale: boolean }>()

  async function loadLatest(chatId: string, signal?: AbortSignal): Promise<void> {
    const load = { stale: false }
    loadingLatest.set(chatId, load)
    openHistory(state.value, chatId)
    try {
      putLatestPage(state.value, chatId, await messageApi.getMessagesPage(chatId, null, signal))
      if (load.stale) putLatestPage(state.value, chatId, await messageApi.getMessagesPage(chatId, null, signal))
    } finally {
      if (loadingLatest.get(chatId) === load) loadingLatest.delete(chatId)
    }
  }

  // An older or newer page is put only next to what it was asked for: a jump or a reload
  // meanwhile replaced the loaded part, and the page would leave a gap in it.

  async function loadOlder(chatId: string): Promise<void> {
    const cursor = state.value.byChat[chatId]?.olderCursor
    if (!state.value.byChat[chatId]?.hasOlder || !cursor) return
    const page = await messageApi.getMessagesPage(chatId, cursor)
    if (state.value.byChat[chatId]?.olderCursor !== cursor) return
    putOlderPage(state.value, chatId, page)
  }

  async function loadContext(chatId: string, messageId: string): Promise<void> {
    putWindow(state.value, chatId, await messageApi.getMessageContext(chatId, messageId))
  }

  async function loadNewer(chatId: string): Promise<void> {
    const history = state.value.byChat[chatId]
    const last = history?.messages.at(-1)
    if (!history?.hasNewer || !last) return
    const page = await messageApi.getMessagesAfter(chatId, last.seq)
    const now = state.value.byChat[chatId]
    if (!now?.hasNewer || now.messages.at(-1)?.seq !== last.seq) return
    putNewerPage(state.value, chatId, page)
  }

  /**
   * newestSeq: the chat's last message, to know whether the page reaches it. False when nothing
   * was written by then: the history stays as it is.
   */
  async function loadAt(chatId: string, date: string, newestSeq: number): Promise<boolean> {
    const page = await messageApi.getMessagesAt(chatId, date)
    const last = page.items.at(-1)
    if (!last) return false
    putWindow(state.value, chatId, { ...page, hasNewer: last.seq < newestSeq })
    return true
  }

  function apply<K extends JournaledEventName>(type: K, payload: JournaledEvents[K], ctx: HistoryContext): void {
    if (type === 'MessageUpdated' || type === 'MessageDeleted' || type === 'ReactionsChanged') {
      const load = loadingLatest.get((payload as { chatId: string }).chatId)
      if (load) load.stale = true
    }
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
