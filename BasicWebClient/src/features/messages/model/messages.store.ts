// The open chat: its history (from the history entity) and the commands on it.

import { computed, ref, watch } from 'vue'
import { defineStore } from 'pinia'

import { useChatsStore } from '@/entities/chat/model/chats.store'
import { useHistoryStore } from '@/entities/message/model/history.store'
import { useHubStore } from '@/shared/api/hub.store'
import * as messagesApi from '../api/messages.api'

export const useMessagesStore = defineStore('messages', () => {
  const hub = useHubStore()
  const chats = useChatsStore()
  const history = useHistoryStore()

  const chatId = ref<string | null>(null)
  const isLoading = ref(false)
  const isLoadingOlder = ref(false)
  const error = ref('')

  const current = computed(() => history.get(chatId.value))
  const messages = computed(() => current.value?.messages ?? [])
  const hasMore = computed(() => current.value?.hasOlder ?? false)

  /** Aborted when switching to another chat. */
  let inFlight: AbortController | null = null

  function reset(): void {
    inFlight?.abort()
    inFlight = null
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

  /** Sent through the hub; the message itself comes back as MessageCreated. */
  async function send(text: string): Promise<boolean> {
    const id = chatId.value
    const trimmed = text.trim()
    if (!id || trimmed.length === 0) return false

    return hub.sendMessage(id, trimmed)
  }

  async function markReadUpToLast(): Promise<void> {
    const id = chatId.value
    const last = messages.value.at(-1)
    if (!id || !last) return

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
    isLoading,
    isLoadingOlder,
    error,
    openChat,
    loadOlder,
    send,
    markReadUpToLast,
    reset,
  }
})
