import { computed, ref } from 'vue'
import { defineStore } from 'pinia'

import type { FolderDto } from '@/shared/api/schema'
import type { JournaledEventName, JournaledEvents } from '@/shared/api/hub.types'
import type { Message } from '@/entities/message/types'
import type { ChatListItem } from '../types'
import * as chatApi from '../api'
import {
  applyChatEvent,
  emptyChats,
  fromSnapshot,
  messageArrived,
  putChat,
  sortedChats,
  type ChatsContext,
  type ChatsEffects,
} from './chats'

/** All chats of the user (archived included) and their folders, kept current by sync. */
export const useChatsStore = defineStore('chats', () => {
  const state = ref(emptyChats())
  const folders = ref<FolderDto[]>([])
  /** false until the first snapshot; snapshotVersion grows with every snapshot (pages may be stale). */
  const loaded = ref(false)
  const snapshotVersion = ref(0)

  const list = computed(() => sortedChats(state.value))

  function get(chatId: string | null | undefined): ChatListItem | null {
    return chatId ? state.value.byId[chatId] ?? null : null
  }

  function replaceAll(chats: ChatListItem[], snapshotFolders: FolderDto[]): void {
    state.value = fromSnapshot(chats)
    folders.value = snapshotFolders
    loaded.value = true
    snapshotVersion.value += 1
  }

  /** A row from the server outside sync (created by us, reloaded). An older row never wins. */
  function put(chat: ChatListItem): void {
    const known = state.value.byId[chat.chatId]
    if (known && (known.lastMessage?.seq ?? 0) > (chat.lastMessage?.seq ?? 0)) {
      // A message event got here before the answer: keep it.
      putChat(state.value, {
        ...chat,
        lastMessage: known.lastMessage,
        lastActivityAt: known.lastActivityAt,
        unreadCount: Math.max(chat.unreadCount, known.unreadCount),
      })
      return
    }
    putChat(state.value, chat)
  }

  function run(effects: ChatsEffects): void {
    for (const chatId of new Set(effects.fetchChats)) {
      chatApi.getChatItem(chatId).then(put).catch(() => {
        // Gone or not ours any more: nothing to show.
      })
    }
  }

  function apply<K extends JournaledEventName>(type: K, payload: JournaledEvents[K], ctx: ChatsContext): void {
    if (type === 'FoldersChanged') {
      folders.value = (payload as JournaledEvents['FoldersChanged']).folders
      return
    }
    const effects: ChatsEffects = { fetchChats: [] }
    applyChatEvent(state.value, type, payload, ctx, effects)
    run(effects)
  }

  /** The ChatListUpdated preview: the full MessageCreated comes with the next catch-up. */
  function preview(message: Message, ctx: ChatsContext): void {
    const effects: ChatsEffects = { fetchChats: [] }
    messageArrived(state.value, message, ctx, effects)
    run(effects)
  }

  /** The user read the chat: counters drop at once, the server confirms with ReadStateChanged. */
  function markReadLocally(chatId: string, seq: number): void {
    const chat = state.value.byId[chatId]
    if (!chat) return
    chat.lastReadSeq = Math.max(chat.lastReadSeq, seq)
    chat.unreadCount = 0
    chat.unreadMentionCount = 0
    chat.markedUnread = false
  }

  /** An optimistic change of one row; the server's event confirms or corrects it. */
  function patch(chatId: string, change: Partial<ChatListItem>): void {
    const chat = state.value.byId[chatId]
    if (chat) Object.assign(chat, change)
  }

  function reset(): void {
    state.value = emptyChats()
    folders.value = []
    loaded.value = false
  }

  return { list, folders, loaded, snapshotVersion, get, replaceAll, put, apply, preview, markReadLocally, patch, reset }
})
