// Which chat is open, and the list actions. The chats themselves live in the chats entity.

import { computed, ref } from 'vue'
import { defineStore } from 'pinia'

import { useChatsStore } from '@/entities/chat/model/chats.store'
import { useAuthStore } from '@/features/auth/model/auth.store'
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

  const auth = useAuthStore()
  const ctx = () => ({ meId: auth.user?.userId ?? '' })

  function pin(chatId: string, pinned: boolean): Promise<void> {
    return command(async () => {
      const result = await chatsApi.setPinned(chatId, pinned)
      chats.apply('PinnedChatsChanged', result, ctx())
      // Pinning takes the chat out of the archive.
      if (pinned) chats.patch(chatId, { archived: false })
    })
  }

  /** Drag and drop of pinned chats: shown at once, the server keeps it. */
  function reorderPinned(chatIds: string[]): Promise<void> {
    chats.apply('PinnedChatsChanged', { chatIds }, ctx())
    return command(() => chatsApi.reorderPinned(chatIds))
  }

  function archive(chatId: string, archived: boolean): Promise<void> {
    // The server unpins an archived chat (PinnedChatsChanged follows).
    if (archived) chats.patch(chatId, { pinnedPosition: null })
    return command(async () => chats.apply('ChatStateChanged', await chatsApi.setArchived(chatId, archived), ctx()))
  }

  /** untilMs: when the mute ends; null — forever. */
  function mute(chatId: string, untilMs: number | null): Promise<void> {
    const until = untilMs === null ? undefined : new Date(untilMs).toISOString()
    return command(async () => chats.apply('ChatStateChanged', await chatsApi.setMuted(chatId, true, until), ctx()))
  }

  function unmute(chatId: string): Promise<void> {
    return command(async () => chats.apply('ChatStateChanged', await chatsApi.setMuted(chatId, false), ctx()))
  }

  function reset(): void {
    selectedChatId.value = null
  }

  return { selectedChatId, selectedChat, select, deselect, openPrivateChat, markUnread, markRead, pin, reorderPinned, archive, mute, unmute, command, reset }
})
