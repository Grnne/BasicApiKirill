/* The single source of truth for which chats exist and their latest message;
   the messages inside a chat belong to the messages store. */

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

  function sortByActivity(): void {
    chats.value.sort(
      (a, b) => parseApiDate(b.lastActivityAt).getTime() - parseApiDate(a.lastActivityAt).getTime(),
    )
  }

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

    // Cleared optimistically; the messages feature sends the actual POST /read.
    const chat = chats.value.find((item) => item.chatId === chatId)
    if (chat) chat.unreadCount = 0

    await hub.joinChat(chatId)
  }

  async function deselect(): Promise<void> {
    selectedChatId.value = null
    await hub.leaveChat()
  }

  async function openPrivateChat(userId: string): Promise<void> {
    const chat = await chatsApi.createPrivateChat(userId)
    upsert(chat)
    await select(chat.chatId)
  }

  function applyListUpdate(chatId: string, message: Message): void {
    const chat = chats.value.find((item) => item.chatId === chatId)

    if (!chat) {
      // The chat appeared while we were offline; fetch just its row.
      void chatsApi
        .getChatItem(chatId)
        .then(upsert)
        .catch(() => {})
      return
    }

    chat.lastMessage = message
    chat.lastActivityAt = message.createdAt

    const isOwn = message.senderId === auth.user?.userId
    const isOpen = chatId === selectedChatId.value
    if (!isOwn && !isOpen) chat.unreadCount += 1

    sortByActivity()
  }

  let isSubscribed = false

  function subscribeToHub(): void {
    if (isSubscribed) return
    isSubscribed = true

    hub.on('ChatListUpdated', applyListUpdate)

    // The payload is a list row already built for the current user.
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
