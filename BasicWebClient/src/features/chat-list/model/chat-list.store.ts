/**
 * Список чатов: загрузка, выбор, живое обновление.
 *
 * Список — единственный источник правды о том, какие чаты есть и что в них
 * последнее. Сообщения внутри чата — забота отдельного стора.
 */

import { computed, ref } from 'vue'
import { defineStore } from 'pinia'

import type { ChatListItem } from '@/entities/chat/types'
import type { Message } from '@/entities/message/types'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { useHubStore } from '@/shared/api/hub.store'
import { parseApiDate } from '@/shared/lib/date'
import * as chatsApi from '../api/chats.api'

export const useChatListStore = defineStore('chatList', () => {
  const auth = useAuthStore()
  const hub = useHubStore()

  const chats = ref<ChatListItem[]>([])
  const selectedChatId = ref<string | null>(null)
  const isLoading = ref(false)
  const loadError = ref('')

  const selectedChat = computed(
    () => chats.value.find((chat) => chat.chatId === selectedChatId.value) ?? null,
  )

  /* ── Работа со списком ── */

  /** Свежие сверху — как в любом мессенджере. */
  function sortByActivity(): void {
    chats.value.sort(
      (a, b) => parseApiDate(b.lastActivityAt).getTime() - parseApiDate(a.lastActivityAt).getTime(),
    )
  }

  /** Добавить чат или заменить существующий. Ключ — chatId. */
  function upsert(item: ChatListItem): void {
    const index = chats.value.findIndex((chat) => chat.chatId === item.chatId)
    if (index === -1) {
      chats.value.push(item)
    } else {
      chats.value[index] = item
    }
    sortByActivity()
  }

  async function load(): Promise<void> {
    isLoading.value = true
    loadError.value = ''
    try {
      chats.value = await chatsApi.getChats()
      sortByActivity()
    } catch {
      loadError.value = 'Не удалось загрузить чаты'
    } finally {
      isLoading.value = false
    }
  }

  async function select(chatId: string): Promise<void> {
    selectedChatId.value = chatId

    // Открытый чат считаем прочитанным — счётчик гасим сразу, не дожидаясь
    // ответа сервера (сам POST /read отправит фича сообщений).
    const chat = chats.value.find((item) => item.chatId === chatId)
    if (chat) chat.unreadCount = 0

    await hub.joinChat(chatId)
  }

  /** Закрыть чат — нужно на узких экранах, чтобы вернуться к списку. */
  async function deselect(): Promise<void> {
    selectedChatId.value = null
    await hub.leaveChat()
  }

  /**
   * Открыть переписку с пользователем. Сервер сам отдаёт существующий чат,
   * если он уже был, поэтому проверять ничего не нужно.
   */
  async function openPrivateChat(userId: string): Promise<void> {
    const chat = await chatsApi.createPrivateChat(userId)
    upsert(chat)
    await select(chat.chatId)
  }

  /* ── События хаба ── */

  function applyListUpdate(chatId: string, message: Message): void {
    const chat = chats.value.find((item) => item.chatId === chatId)

    if (!chat) {
      // Чата нет в списке — значит он появился, пока мы были не в сети.
      // Догружаем одну строку, а не весь список.
      void chatsApi
        .getChatItem(chatId)
        .then(upsert)
        .catch(() => {})
      return
    }

    chat.lastMessage = message
    chat.lastActivityAt = message.createdAt

    // Свои сообщения и сообщения в открытом чате непрочитанными не считаем.
    const isOwn = message.senderId === auth.user?.userId
    const isOpen = chatId === selectedChatId.value
    if (!isOwn && !isOpen) chat.unreadCount += 1

    sortByActivity()
  }

  let isSubscribed = false

  /** Подписки на хаб. Вызывается один раз — при первой загрузке списка. */
  function subscribeToHub(): void {
    if (isSubscribed) return
    isSubscribed = true

    hub.on('ChatListUpdated', applyListUpdate)

    // Payload — готовая строка списка, собранная сервером под нас.
    hub.on('ChatCreated', upsert)
  }

  function reset(): void {
    chats.value = []
    selectedChatId.value = null
    loadError.value = ''
  }

  return {
    chats,
    selectedChatId,
    selectedChat,
    isLoading,
    loadError,
    load,
    select,
    deselect,
    openPrivateChat,
    subscribeToHub,
    reset,
  }
})
