// The open chat: its history (from the history entity) and the commands on it.

import { computed, ref, watch } from 'vue'
import { defineStore } from 'pinia'

import { useChatsStore } from '@/entities/chat/model/chats.store'
import { useHistoryStore } from '@/entities/message/model/history.store'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { uuid } from '@/shared/lib/uuid'
import * as messagesApi from '../api/messages.api'
import { Outbox } from './outbox'

export const useMessagesStore = defineStore('messages', () => {
  const auth = useAuthStore()
  const chats = useChatsStore()
  const history = useHistoryStore()

  const outbox = new Outbox(
    { send: messagesApi.sendMessage },
    {
      stored: (message) => {
        history.stored(message)
        chats.preview(message, { meId: auth.user?.userId ?? '' })
      },
      sending: (chatId, id) => history.updatePending(chatId, id, { state: 'sending', error: null }),
      failed: (chatId, id, error) => history.updatePending(chatId, id, { state: 'failed', error }),
    },
  )

  const chatId = ref<string | null>(null)
  const isLoading = ref(false)
  const isLoadingOlder = ref(false)
  const error = ref('')

  const current = computed(() => history.get(chatId.value))
  const messages = computed(() => current.value?.messages ?? [])
  const hasMore = computed(() => current.value?.hasOlder ?? false)
  const pending = computed(() => current.value?.pending ?? [])

  /** Aborted when switching to another chat. */
  let inFlight: AbortController | null = null

  function reset(): void {
    inFlight?.abort()
    inFlight = null
    clearTimeout(readTimer)
    chatId.value = null
    error.value = ''
    isLoading.value = false
  }

  async function loadLatest(id: string): Promise<void> {
    inFlight?.abort()
    const controller = new AbortController()
    inFlight = controller
    error.value = ''
    // Cached messages stay on screen while the newest page loads.
    isLoading.value = messages.value.length === 0

    try {
      await history.loadLatest(id, controller.signal)
      if (controller.signal.aborted || chatId.value !== id) return
      await markReadUpToLast()
    } catch {
      if (!controller.signal.aborted) error.value = 'Не удалось загрузить сообщения'
    } finally {
      if (!controller.signal.aborted) isLoading.value = false
    }
  }

  async function openChat(id: string): Promise<void> {
    chatId.value = id
    await loadLatest(id)
  }

  // A new snapshot means a long disconnect: the cached pages may have gaps.
  watch(
    () => chats.snapshotVersion,
    () => {
      if (chatId.value) void loadLatest(chatId.value)
    },
  )

  async function loadOlder(): Promise<void> {
    const id = chatId.value
    if (!id || isLoadingOlder.value) return

    isLoadingOlder.value = true
    try {
      await history.loadOlder(id)
    } catch {
      // Ignored: scrolling up again retries.
    } finally {
      isLoadingOlder.value = false
    }
  }

  /** Shows the message at once as "sending"; the stored one replaces it by clientMessageId. */
  function send(text: string): boolean {
    const id = chatId.value
    const trimmed = text.trim()
    if (!id || trimmed.length === 0) return false

    const message = {
      clientMessageId: uuid(),
      chatId: id,
      text: trimmed,
      entities: [],
      replyToMessageId: null,
      createdAt: new Date().toISOString(),
      state: 'sending' as const,
      error: null,
    }
    history.addPending(message)
    void outbox.deliver(message)
    return true
  }

  function retry(clientMessageId: string): void {
    const message = pending.value.find((p) => p.clientMessageId === clientMessageId)
    if (message && message.state === 'failed') void outbox.deliver(message)
  }

  function discard(clientMessageId: string): void {
    if (chatId.value) history.removePending(chatId.value, clientMessageId)
  }

  /** Pause before /read: a burst of messages on screen is one request. */
  const READ_DELAY_MS = 500
  let readTimer: ReturnType<typeof setTimeout> | undefined

  /** The newest message is on screen (the list is at the bottom of a visible tab). */
  function seen(): void {
    clearTimeout(readTimer)
    readTimer = setTimeout(() => void markReadUpToLast(), READ_DELAY_MS)
  }

  async function markReadUpToLast(): Promise<void> {
    const id = chatId.value
    const last = messages.value.at(-1)
    const chat = chats.get(id)
    if (!id || !last) return

    const unread =
      !chat || chat.unreadCount > 0 || chat.markedUnread ||
      (last.senderId !== auth.user?.userId && last.seq > chat.lastReadSeq)
    if (!unread) return

    chats.markReadLocally(id, last.seq)
    try {
      await messagesApi.markRead(id, last.id)
    } catch {
      // Not critical: the read mark is sent again next time the chat is opened.
    }
  }

  return {
    chatId,
    messages,
    hasMore,
    pending,
    isLoading,
    isLoadingOlder,
    error,
    openChat,
    loadOlder,
    send,
    retry,
    discard,
    markReadUpToLast,
    seen,
    reset,
  }
})
