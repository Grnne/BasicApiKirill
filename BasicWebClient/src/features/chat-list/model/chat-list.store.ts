// Which chat is open, and the list actions. The chats themselves live in the chats entity.

import { computed, ref } from 'vue'
import { defineStore } from 'pinia'

import { useChatsStore } from '@/entities/chat/model/chats.store'
import { useHubStore } from '@/shared/api/hub.store'
import { describeError } from '@/shared/api/problem'
import { useNoticesStore } from '@/shared/ui/notices.store'
import * as chatsApi from '../api/chats.api'

export const useChatListStore = defineStore('chatList', () => {
  const hub = useHubStore()
  const chats = useChatsStore()
  const notices = useNoticesStore()

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

  /** Runs a list command; on failure the server's next event (or a reload) restores the truth. */
  async function command(run: () => Promise<void>): Promise<void> {
    try {
      await run()
    } catch (e) {
      notices.push(describeError(e))
    }
  }

  function markUnread(chatId: string): Promise<void> {
    chats.patch(chatId, { markedUnread: true })
    return command(() => chatsApi.setMarkedUnread(chatId, true))
  }

  function markRead(chatId: string): Promise<void> {
    const chat = chats.get(chatId)
    if (!chat) return Promise.resolve()
    const last = chat.lastMessage
    chats.markReadLocally(chatId, last?.seq ?? chat.lastReadSeq)
    return command(() =>
      last ? chatsApi.markRead(chatId, last.id) : chatsApi.setMarkedUnread(chatId, false),
    )
  }

  function reset(): void {
    selectedChatId.value = null
  }

  return { selectedChatId, selectedChat, select, deselect, openPrivateChat, markUnread, markRead, command, reset }
})
