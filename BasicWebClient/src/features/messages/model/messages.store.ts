// The open chat: its history (from the history entity) and the commands on it.

import { computed, ref, shallowRef, watch } from 'vue'
import { defineStore } from 'pinia'

import { useChatsStore } from '@/entities/chat/model/chats.store'
import { useHistoryStore } from '@/entities/message/model/history.store'
import { useAuthStore } from '@/features/auth/model/auth.store'
import type { Message } from '@/entities/message/types'
import { describeError } from '@/shared/api/problem'
import { uuid } from '@/shared/lib/uuid'
import { useNoticesStore } from '@/shared/ui/notices.store'
import * as messagesApi from '../api/messages.api'
import { Outbox } from './outbox'

export const useMessagesStore = defineStore('messages', () => {
  const auth = useAuthStore()
  const chats = useChatsStore()
  const history = useHistoryStore()
  const notices = useNoticesStore()
  const ctx = () => ({ meId: auth.user?.userId ?? '' })

  const outbox = new Outbox(
    { send: messagesApi.sendMessage },
    {
      stored: (message) => {
        history.stored(message)
        chats.preview(message, ctx())
      },
      sending: (chatId, id) => history.updatePending(chatId, id, { state: 'sending', error: null }),
      failed: (chatId, id, error) => history.updatePending(chatId, id, { state: 'failed', error }),
    },
  )

  const chatId = ref<string | null>(null)
  const isLoading = ref(false)
  const isLoadingOlder = ref(false)
  const error = ref('')
  /** The message being edited in the composer. */
  const editing = shallowRef<Message | null>(null)
  /** The message the next one answers. */
  const replyTo = shallowRef<Message | null>(null)
  /** Messages picked for forwarding; non-empty means selection mode. */
  const selected = ref<Set<string>>(new Set())
  /** A message the list should scroll to and highlight. */
  const jumpTarget = ref<string | null>(null)

  const current = computed(() => history.get(chatId.value))
  const messages = computed(() => current.value?.messages ?? [])
  const hasMore = computed(() => current.value?.hasOlder ?? false)
  const pending = computed(() => current.value?.pending ?? [])

  /** Aborted when switching to another chat. */
  let inFlight: AbortController | null = null

  function reset(): void {
    inFlight?.abort()
    inFlight = null
    clearTimeout(readTimer)
    editing.value = null
    replyTo.value = null
    selected.value = new Set()
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
    if (chatId.value !== id) {
      editing.value = null
      replyTo.value = null
      selected.value = new Set()
    }
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

  /** Shows the message at once as "sending"; the stored one replaces it by clientMessageId. */
  function send(text: string): boolean {
    const id = chatId.value
    const trimmed = text.trim()
    if (!id || trimmed.length === 0) return false

    const message = {
      clientMessageId: uuid(),
      chatId: id,
      text: trimmed,
      entities: [],
      replyToMessageId: replyTo.value?.chatId === id ? replyTo.value.id : null,
      createdAt: new Date().toISOString(),
      state: 'sending' as const,
      error: null,
    }
    history.addPending(message)
    replyTo.value = null
    void outbox.deliver(message)
    return true
  }

  function startReply(message: Message): void {
    editing.value = null
    replyTo.value = message
  }

  function cancelReply(): void {
    replyTo.value = null
  }

  function toggleSelected(id: string): void {
    const next = new Set(selected.value)
    if (next.has(id)) next.delete(id)
    else next.add(id)
    selected.value = next
  }

  function clearSelection(): void {
    selected.value = new Set()
  }

  /** Forwards the given messages of the open chat; returns how many arrived. */
  async function forward(targetChatId: string, ids: readonly string[]): Promise<number> {
    const fromChatId = chatId.value
    if (!fromChatId || ids.length === 0) return 0
    // The server keeps the source order (by seq) whatever the order here; clientMessageIds go with it.
    const ordered = messages.value.filter((m) => ids.includes(m.id)).map((m) => m.id)
    try {
      const response = await messagesApi.forwardMessages(targetChatId, {
        fromChatId,
        messageIds: ordered,
        clientMessageIds: ordered.map(() => uuid()),
      })
      for (const message of response.items) {
        history.stored(message)
        chats.preview(message, ctx())
      }
      clearSelection()
      return response.items.length
    } catch (e) {
      notices.push(describeError(e))
      return 0
    }
  }

  /** Scroll to a message; older pages are loaded until it is found (or the history ends). */
  async function jumpTo(messageId: string): Promise<void> {
    const MAX_PAGES = 10
    for (let page = 0; page < MAX_PAGES && !messages.value.some((m) => m.id === messageId); page++) {
      if (!hasMore.value) break
      await loadOlder()
    }
    if (messages.value.some((m) => m.id === messageId)) jumpTarget.value = messageId
    else notices.push('Исходное сообщение не найдено', 'info')
  }

  function startEdit(message: Message): void {
    replyTo.value = null
    editing.value = message
  }

  function cancelEdit(): void {
    editing.value = null
  }

  /** The server's answer is applied at once; MessageUpdated brings the same to other devices. */
  async function saveEdit(text: string): Promise<boolean> {
    const target = editing.value
    const trimmed = text.trim()
    if (!target || trimmed.length === 0) return false
    if (trimmed === target.text) {
      editing.value = null
      return true
    }
    try {
      const updated = await messagesApi.editMessage(target.chatId, target.id, { text: trimmed })
      history.apply('MessageUpdated', updated, ctx())
      chats.apply('MessageUpdated', updated, ctx())
      editing.value = null
      return true
    } catch (e) {
      notices.push(describeError(e))
      return false
    }
  }

  async function remove(message: Message, forEveryone: boolean): Promise<void> {
    try {
      await messagesApi.deleteMessage(message.chatId, message.id, forEveryone)
      const deleted = { chatId: message.chatId, messageId: message.id, seq: message.seq, forEveryone }
      history.apply('MessageDeleted', deleted, ctx())
      chats.apply('MessageDeleted', deleted, ctx())
      if (editing.value?.id === message.id) editing.value = null
    } catch (e) {
      notices.push(describeError(e))
    }
  }

  /** The same emoji again takes the reaction off. */
  async function react(message: Message, emoji: string): Promise<void> {
    const me = ctx()
    try {
      if (message.myReaction === emoji) {
        await messagesApi.removeReaction(message.chatId, message.id)
        // 204 carries no summary: count it here; ReactionsChanged brings the server's.
        const reactions = message.reactions
          .map((r) => (r.emoji === emoji ? { ...r, count: r.count - 1 } : r))
          .filter((r) => r.count > 0)
        history.apply('ReactionsChanged', { chatId: message.chatId, messageId: message.id, userId: me.meId, emoji: null, reactions }, me)
      } else {
        const summary = await messagesApi.setReaction(message.chatId, message.id, emoji)
        history.apply('ReactionsChanged', summary, me)
      }
    } catch (e) {
      notices.push(describeError(e))
    }
  }

  function retry(clientMessageId: string): void {
    const message = pending.value.find((p) => p.clientMessageId === clientMessageId)
    if (message && message.state === 'failed') void outbox.deliver(message)
  }

  function discard(clientMessageId: string): void {
    if (chatId.value) history.removePending(chatId.value, clientMessageId)
  }

  /** Pause before /read: a burst of messages on screen is one request. */
  const READ_DELAY_MS = 500
  let readTimer: ReturnType<typeof setTimeout> | undefined

  /** The newest message is on screen (the list is at the bottom of a visible tab). */
  function seen(): void {
    clearTimeout(readTimer)
    readTimer = setTimeout(() => void markReadUpToLast(), READ_DELAY_MS)
  }

  async function markReadUpToLast(): Promise<void> {
    const id = chatId.value
    const last = messages.value.at(-1)
    const chat = chats.get(id)
    if (!id || !last) return

    const unread =
      !chat || chat.unreadCount > 0 || chat.markedUnread ||
      (last.senderId !== auth.user?.userId && last.seq > chat.lastReadSeq)
    if (!unread) return

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
    pending,
    editing,
    replyTo,
    selected,
    jumpTarget,
    isLoading,
    isLoadingOlder,
    error,
    openChat,
    loadOlder,
    send,
    retry,
    discard,
    startEdit,
    startReply,
    cancelReply,
    toggleSelected,
    clearSelection,
    forward,
    jumpTo,
    react,
    cancelEdit,
    saveEdit,
    remove,
    markReadUpToLast,
    seen,
    reset,
  }
})
