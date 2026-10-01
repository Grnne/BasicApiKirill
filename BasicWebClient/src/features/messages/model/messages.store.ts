// The open chat: its history (from the history entity) and the commands on it.

import { computed, markRaw, ref, shallowRef, watch } from 'vue'
import { defineStore } from 'pinia'

import { useChatsStore } from '@/entities/chat/model/chats.store'
import { useHistoryStore } from '@/entities/message/model/history.store'
import { useAuthStore } from '@/features/auth/model/auth.store'
import type { Message } from '@/entities/message/types'
import type { AttachmentDto, MessageEntityDto } from '@/shared/api/schema'
import { ApiError, NetworkError, describeError } from '@/shared/api/problem'
import { uuid } from '@/shared/lib/uuid'
import { useNoticesStore } from '@/shared/ui/notices.store'
import * as messagesApi from '../api/messages.api'
import { DraftSaver } from './drafts'
import { Outbox } from './outbox'

export const useMessagesStore = defineStore('messages', () => {
  const auth = useAuthStore()
  const chats = useChatsStore()
  const history = useHistoryStore()
  const notices = useNoticesStore()
  const ctx = () => ({ meId: auth.user?.userId ?? '' })

  /**
   * Chats whose other side does not let the user write (their privacy, or they blocked the user —
   * the server tells both apart from nobody). Known only from a refused send; kept until a message
   * goes through or the user tries again.
   */
  const restricted = ref<ReadonlySet<string>>(new Set())
  function setRestricted(id: string, on: boolean): void {
    if (restricted.value.has(id) === on) return
    const next = new Set(restricted.value)
    if (on) next.add(id)
    else next.delete(id)
    restricted.value = next
  }

  const outbox = new Outbox(
    { send: messagesApi.sendMessage },
    {
      stored: (message) => {
        history.stored(message)
        chats.messageStored(message, ctx())
        setRestricted(message.chatId, false)
      },
      sending: (chatId, id) => history.updatePending(chatId, id, { state: 'sending', error: null }),
      failed: (chatId, id, error, code) => {
        history.updatePending(chatId, id, { state: 'failed', error })
        if (code === 'PRIVACY_RESTRICTED') setRestricted(chatId, true)
      },
    },
  )

  const drafts = markRaw(
    new DraftSaver(
      { save: messagesApi.saveDraft, remove: messagesApi.removeDraft },
      (id, draft) => chats.patch(id, { draft }),
    ),
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
  /** Open the next chat at this message instead of its newest page (a global search hit). */
  let pendingJump: { chatId: string; messageId: string } | null = null

  const current = computed(() => history.get(chatId.value))
  const messages = computed(() => current.value?.messages ?? [])
  const hasMore = computed(() => current.value?.hasOlder ?? false)
  /** The loaded part is in the middle: newer pages load as the list scrolls down. */
  const hasNewer = computed(() => current.value?.hasNewer ?? false)
  const isLoadingNewer = ref(false)
  const pending = computed(() => current.value?.pending ?? [])

  /** Aborted when switching to another chat. */
  let inFlight: AbortController | null = null

  // A failed load of the open chat is repeated by itself: after a server or network hiccup the
  // chat would otherwise stay empty until the user opened another one.
  const RELOAD_DELAYS_MS = [2_000, 5_000, 15_000, 30_000]
  let reloadTimer: ReturnType<typeof setTimeout> | undefined
  let reloadAttempt = 0

  function reset(): void {
    inFlight?.abort()
    inFlight = null
    clearTimeout(reloadTimer)
    reloadAttempt = 0
    clearTimeout(readTimer)
    editing.value = null
    replyTo.value = null
    selected.value = new Set()
    chatId.value = null
    error.value = ''
    isLoading.value = false
    restricted.value = new Set()
  }

  async function loadLatest(id: string): Promise<void> {
    clearTimeout(reloadTimer)
    inFlight?.abort()
    const controller = new AbortController()
    inFlight = controller
    error.value = ''
    // Cached messages stay on screen while the newest page loads.
    isLoading.value = messages.value.length === 0

    try {
      await history.loadLatest(id, controller.signal)
      if (controller.signal.aborted || chatId.value !== id) return
      reloadAttempt = 0
      await markReadUpToLast()
    } catch (e) {
      if (controller.signal.aborted) return
      error.value = 'Не удалось загрузить сообщения'
      const transient = e instanceof NetworkError || (e instanceof ApiError && (e.status >= 500 || e.status === 429))
      if (transient) {
        const delay = RELOAD_DELAYS_MS[Math.min(reloadAttempt, RELOAD_DELAYS_MS.length - 1)]
        reloadAttempt += 1
        reloadTimer = setTimeout(() => {
          if (chatId.value === id) void loadLatest(id)
        }, delay)
      }
    } finally {
      if (!controller.signal.aborted) isLoading.value = false
    }
  }

  function requestJump(id: string, messageId: string): void {
    pendingJump = { chatId: id, messageId }
  }

  async function openChat(id: string): Promise<void> {
    const jump = pendingJump?.chatId === id ? pendingJump.messageId : null
    pendingJump = null
    if (chatId.value !== id) {
      reloadAttempt = 0
      editing.value = null
      replyTo.value = null
      selected.value = new Set()
    }
    chatId.value = id
    if (jump) await jumpTo(jump)
    else await loadLatest(id)
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

  /**
   * Shows the message at once as "sending"; the stored one replaces it by clientMessageId.
   * The text goes as typed: the server trims it and moves the entities with it.
   */
  function send(text: string, entities: MessageEntityDto[] = [], attachments: AttachmentDto[] = []): boolean {
    const id = chatId.value
    // With files the text is a caption and may be empty.
    if (!id || (text.trim().length === 0 && attachments.length === 0)) return false

    const message = {
      clientMessageId: uuid(),
      chatId: id,
      text,
      entities,
      replyToMessageId: replyTo.value?.chatId === id ? replyTo.value.id : null,
      attachments,
      createdAt: new Date().toISOString(),
      state: 'sending' as const,
      error: null,
    }
    history.addPending(message)
    replyTo.value = null
    // Sending removes the draft on the server (and DraftUpdated tells the other devices).
    drafts.cancel(id)
    chats.patch(id, { draft: null })
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
        chats.messageStored(message, ctx())
      }
      clearSelection()
      return response.items.length
    } catch (e) {
      notices.push(describeError(e))
      return 0
    }
  }

  /** Scroll to a message; one not loaded opens the history around it. */
  async function jumpTo(messageId: string): Promise<void> {
    const id = chatId.value
    if (!id) return
    if (!messages.value.some((m) => m.id === messageId)) {
      try {
        await history.loadContext(id, messageId)
      } catch (e) {
        notices.push(describeError(e), 'info')
        return
      }
    }
    if (chatId.value === id) jumpTarget.value = messageId
  }

  /** The history at a moment: the last message at or before it comes into view. */
  async function jumpToDate(date: Date): Promise<void> {
    const id = chatId.value
    if (!id) return
    try {
      await history.loadAt(id, date.toISOString(), chats.get(id)?.lastMessage?.seq ?? 0)
      const target = messages.value.at(-1)
      if (target && chatId.value === id) jumpTarget.value = target.id
      else if (!target) notices.push('В этот день сообщений ещё не было', 'info')
    } catch (e) {
      notices.push(describeError(e))
    }
  }

  async function loadNewer(): Promise<void> {
    const id = chatId.value
    if (!id || isLoadingNewer.value || !hasNewer.value) return
    isLoadingNewer.value = true
    try {
      await history.loadNewer(id)
    } catch {
      // Scrolling down again retries.
    } finally {
      isLoadingNewer.value = false
    }
  }

  /** Back from a window in the middle to the newest messages. */
  async function backToLatest(): Promise<void> {
    const id = chatId.value
    if (!id) return
    await loadLatest(id)
    const last = messages.value.at(-1)
    if (last) jumpTarget.value = last.id
  }

  function startEdit(message: Message): void {
    replyTo.value = null
    editing.value = message
  }

  function cancelEdit(): void {
    editing.value = null
  }

  /** The server's answer is applied at once; MessageUpdated brings the same to other devices. */
  async function saveEdit(text: string, entities: MessageEntityDto[] = []): Promise<boolean> {
    const target = editing.value
    if (!target || text.trim().length === 0) return false
    if (text === target.text && JSON.stringify(entities) === JSON.stringify(target.entities)) {
      editing.value = null
      return true
    }
    try {
      // An edit replaces the formatting too: the entities go with the text, or it becomes plain.
      const updated = await messagesApi.editMessage(target.chatId, target.id, { text, entities })
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

  const isRestricted = (id: string | null) => !!id && restricted.value.has(id)
  const tryAgain = (id: string) => setRestricted(id, false)

  /** "Retry" after a failed load. */
  function reload(): void {
    if (chatId.value) void loadLatest(chatId.value)
  }

  return {
    reload,
    isRestricted,
    tryAgain,
    chatId,
    messages,
    hasMore,
    hasNewer,
    isLoadingNewer,
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
    jumpToDate,
    requestJump,
    loadNewer,
    backToLatest,
    react,
    drafts,
    cancelEdit,
    saveEdit,
    remove,
    markReadUpToLast,
    seen,
    reset,
  }
})
