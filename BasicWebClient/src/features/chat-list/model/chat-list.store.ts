// Which chat is open, and the list actions. The chats themselves live in the chats entity.

import { computed, ref } from 'vue'
import { defineStore } from 'pinia'

import { useChatsStore } from '@/entities/chat/model/chats.store'
import { useHubStore } from '@/shared/api/hub.store'
import * as chatsApi from '../api/chats.api'

export const useChatListStore = defineStore('chatList', () => {
  const hub = useHubStore()
  const chats = useChatsStore()

  const selectedChatId = ref<string | null>(null)
  const selectedChat = computed(() => chats.get(selectedChatId.value))

  async function select(chatId: string): Promise<void> {
    selectedChatId.value = chatId
    // MessageCreated goes only to the connections that joined the chat's group.
    await hub.joinChat(chatId)
  }

  async function deselect(): Promise<void> {
    selectedChatId.value = null
    await hub.leaveChat()
  }

  /** The server returns the existing private chat if there is one. */
  async function openPrivateChat(userId: string): Promise<void> {
    const chat = await chatsApi.createPrivateChat(userId)
    chats.put(chat)
    await select(chat.chatId)
  }

  function reset(): void {
    selectedChatId.value = null
  }

  return { selectedChatId, selectedChat, select, deselect, openPrivateChat, reset }
})
