/* Messages of the open chat, oldest to newest as on screen. Pages arrive going back in time,
   so older pages are prepended. */

import { ref, shallowRef } from 'vue'
import { defineStore } from 'pinia'

import type { Message } from '@/entities/message/types'
import { useHubStore } from '@/shared/api/hub.store'
import * as messagesApi from '../api/messages.api'

export const useMessagesStore = defineStore('messages', () => {
  const hub = useHubStore()

  const chatId = ref<string | null>(null)
  const messages = ref<Message[]>([])
  const hasMore = ref(false)
  const isLoading = ref(false)
  const isLoadingOlder = ref(false)
  const error = ref('')

  const nextCursor = shallowRef<string | null>(null)

  /** Aborted when switching to another chat. */
  let inFlight: AbortController | null = null

  function reset(): void {
    inFlight?.abort()
    inFlight = null
    chatId.value = null
    messages.value = []
    nextCursor.value = null
    hasMore.value = false
    error.value = ''
  }

  async function openChat(id: string): Promise<void> {
    inFlight?.abort()
    const controller = new AbortController()
    inFlight = controller

    chatId.value = id
    messages.value = []
    nextCursor.value = null
    hasMore.value = false
    error.value = ''
    isLoading.value = true

    try {
      const page = await messagesApi.getMessagesPage(id, null, controller.signal)
      // The user may have switched chats while this was loading.
      if (controller.signal.aborted || chatId.value !== id) return

      messages.value = page.items
      nextCursor.value = page.nextCursor
      hasMore.value = page.hasMore
      await markReadUpToLast()
    } catch {
      if (!controller.signal.aborted) error.value = 'Не удалось загрузить сообщения'
    } finally {
      if (!controller.signal.aborted) isLoading.value = false
    }
  }

  async function loadOlder(): Promise<void> {
    const id = chatId.value
    const cursor = nextCursor.value
    if (!id || !cursor || isLoadingOlder.value) return

    isLoadingOlder.value = true
    try {
      const page = await messagesApi.getMessagesPage(id, cursor)
      if (chatId.value !== id) return

      messages.value = [...page.items, ...messages.value]
      nextCursor.value = page.nextCursor
      hasMore.value = page.hasMore
    } catch {
      // Ignored: scrolling up again retries.
    } finally {
      isLoadingOlder.value = false
    }
  }

  /**
   * Sent through the hub, and nothing is appended here: the server broadcasts MessageCreated to
   * the group including us, so our own message arrives the same way as others' and cannot diverge.
   */
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

    try {
      await messagesApi.markRead(id, last.id)
    } catch {
      // Not critical: the read mark is sent again next time the chat is opened.
    }
  }

  function appendLive(message: Message): void {
    if (message.chatId !== chatId.value) return
    // The message may already be in a loaded page.
    if (messages.value.some((item) => item.id === message.id)) return
    messages.value.push(message)
  }

  let isSubscribed = false

  function subscribeToHub(): void {
    if (isSubscribed) return
    isSubscribed = true
    hub.on('MessageCreated', appendLive)
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
    subscribeToHub,
    reset,
  }
})
