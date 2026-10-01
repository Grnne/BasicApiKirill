// Which chat is open, and the list actions. The chats themselves live in the chats entity.

import { computed, ref, watch } from 'vue'
import { defineStore } from 'pinia'

import * as chatApi from '@/entities/chat/api'
import { useChatsStore } from '@/entities/chat/model/chats.store'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { useHubStore } from '@/shared/api/hub.store'
import { describeError } from '@/shared/api/problem'
import { useNoticesStore } from '@/shared/ui/notices.store'
import type { FolderDto, SaveFolderDto } from '@/shared/api/schema'
import * as chatsApi from '../api/chats.api'
import * as foldersApi from '../api/folders.api'

/** Long enough for a request to be answered; a failed one is forgotten by then. */
const EXPECT_GONE_MS = 10_000

export const useChatListStore = defineStore('chatList', () => {
  const hub = useHubStore()
  const chats = useChatsStore()
  const notices = useNoticesStore()

  const selectedChatId = ref<string | null>(null)
  /** The folder tab shown; null — all chats. */
  const selectedFolderId = ref<string | null>(null)
  const selectedFolder = computed(() => chats.folders.find((f) => f.id === selectedFolderId.value) ?? null)
  const selectedChat = computed(() => chats.get(selectedChatId.value))

  /**
   * chatId -> until when its going away is the user's own doing (left or deleted the group). The
   * hub event may come before the answer to the request, so it is marked before the request.
   */
  const expectedGone = new Map<string, number>()
  function expectGone(chatId: string): void {
    expectedGone.set(chatId, Date.now() + EXPECT_GONE_MS)
  }

  // The open chat left the list (removed from the group, the group deleted): say so and close it.
  watch(selectedChat, (now, before) => {
    if (now || !before || selectedChatId.value !== before.chatId) return
    const own = (expectedGone.get(before.chatId) ?? 0) > Date.now()
    expectedGone.delete(before.chatId)
    void deselect()
    if (own) return
    notices.push(
      before.type === 'group' ? `Вы больше не участник группы «${before.title ?? ''}»` : 'Чат больше недоступен',
      'info',
    )
  })

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

  async function openSaved(): Promise<void> {
    try {
      const chat = await chatApi.openSavedChat()
      chats.put(chat)
      await select(chat.chatId)
    } catch (e) {
      notices.push(describeError(e))
    }
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

  function setFolders(folders: FolderDto[]): void {
    chats.apply('FoldersChanged', { folders }, ctx())
    if (selectedFolderId.value && !folders.some((f) => f.id === selectedFolderId.value)) selectedFolderId.value = null
  }

  /** Saves a new folder (no id) or changes one; FoldersChanged brings the same to other devices. */
  async function saveFolder(id: string | null, body: SaveFolderDto): Promise<boolean> {
    try {
      const saved = id ? await foldersApi.updateFolder(id, body) : await foldersApi.createFolder(body)
      const others = chats.folders.filter((f) => f.id !== saved.id)
      const index = chats.folders.findIndex((f) => f.id === saved.id)
      setFolders(index === -1 ? [...others, saved] : chats.folders.map((f) => (f.id === saved.id ? saved : f)))
      return true
    } catch (e) {
      notices.push(describeError(e))
      return false
    }
  }

  function deleteFolder(id: string): Promise<void> {
    return command(async () => {
      await foldersApi.deleteFolder(id)
      setFolders(chats.folders.filter((f) => f.id !== id))
    })
  }

  function reorderFolders(ids: string[]): Promise<void> {
    setFolders(ids.map((id) => chats.folders.find((f) => f.id === id)).filter((f): f is FolderDto => !!f))
    return command(async () => setFolders(await foldersApi.reorderFolders(ids)))
  }

  /** Pinned inside the folder (on top of it), apart from the global pins. */
  function pinInFolder(folder: FolderDto, chatId: string, pinned: boolean): Promise<void> {
    const pinnedChatIds = pinned
      ? [chatId, ...folder.pinnedChatIds.filter((id) => id !== chatId)]
      : folder.pinnedChatIds.filter((id) => id !== chatId)
    return saveFolder(folder.id, { pinnedChatIds }).then(() => {})
  }

  function reset(): void {
    selectedChatId.value = null
    selectedFolderId.value = null
  }

  return {
    selectedFolderId,
    selectedFolder,
    saveFolder,
    deleteFolder,
    reorderFolders,
    pinInFolder,
    selectedChatId,
    selectedChat,
    select, deselect, expectGone, openPrivateChat, openSaved, markUnread, markRead, pin, reorderPinned, archive, mute, unmute, command, reset }
})
