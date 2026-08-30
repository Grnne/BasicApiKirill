/**
 * Лента сообщений открытого чата.
 *
 * Порядок в messages — от старых к новым, как на экране. Страницы приходят
 * назад по времени, поэтому подгруженные сообщения добавляются В НАЧАЛО.
 */

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

  /** Запрос текущего чата. Отменяется при переключении на другой. */
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

  /** Открыть чат и показать последнюю страницу. */
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
      // Пока грузили, пользователь мог переключиться на другой чат.
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

  /** Подгрузить страницу постарше — вызывается при прокрутке вверх. */
  async function loadOlder(): Promise<void> {
    const id = chatId.value
    const cursor = nextCursor.value
    if (!id || !cursor || isLoadingOlder.value) return

    isLoadingOlder.value = true
    try {
      const page = await messagesApi.getMessagesPage(id, cursor)
      if (chatId.value !== id) return

      // Именно в начало: страница старее того, что уже на экране.
      messages.value = [...page.items, ...messages.value]
      nextCursor.value = page.nextCursor
      hasMore.value = page.hasMore
    } catch {
      // Молча: пользователь просто попробует прокрутить ещё раз.
    } finally {
      isLoadingOlder.value = false
    }
  }

  /**
   * Отправка идёт через хаб, а не через REST. Сервер разошлёт MessageCreated
   * всем в группе, включая нас, — поэтому в ленту здесь ничего не добавляем.
   * Своё сообщение придёт тем же путём, что и чужие, и не разъедется с сервером.
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
      // Не критично: отметка о прочтении повторится при следующем открытии.
    }
  }

  /* ── События хаба ── */

  function appendLive(message: Message): void {
    if (message.chatId !== chatId.value) return
    // Защита от дубля: сообщение могло попасть и в загруженную страницу.
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
