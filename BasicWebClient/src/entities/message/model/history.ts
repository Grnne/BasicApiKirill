// Loaded message history per chat as a pure state machine: pages + events -> state.
// Events are idempotent (by message id) because they may arrive twice: live and on catch-up.

import type {
  AttachmentDto,
  MessageDto,
  MessageEntityDto,
  MessageDtoCursorPaginatedResponse,
  MessageWindowDto,
} from '@/shared/api/schema'
import type { JournaledEvents } from '@/shared/api/hub.types'

/** A message sent from this device that the server has not confirmed yet. */
export interface PendingMessage {
  clientMessageId: string
  chatId: string
  text: string
  entities: MessageEntityDto[]
  replyToMessageId: string | null
  /** Uploaded files of the message (an album), in order. */
  attachments: AttachmentDto[]
  createdAt: string
  state: 'sending' | 'failed'
  /** Why the last attempt failed, for the user. */
  error: string | null
}

export interface ChatHistory {
  /** Server messages in ascending seq, without gaps between the loaded ones. */
  messages: MessageDto[]
  pending: PendingMessage[]
  /** Cursor to the page before messages[0]. */
  olderCursor: string | null
  hasOlder: boolean
  /** The loaded part ends before the newest message (opened at a search hit or a date). */
  hasNewer: boolean
}

export interface HistoryState {
  byChat: Record<string, ChatHistory>
}

export interface HistoryContext {
  meId: string
}

export function emptyHistory(): HistoryState {
  return { byChat: {} }
}

function historyOf(state: HistoryState, chatId: string): ChatHistory {
  let history = state.byChat[chatId]
  if (!history) {
    history = { messages: [], pending: [], olderCursor: null, hasOlder: false, hasNewer: false }
    state.byChat[chatId] = history
  }
  return history
}

const bySeq = (a: MessageDto, b: MessageDto) => a.seq - b.seq

/**
 * The newest page of a chat. Kept from before: messages newer than the page (arrived live while
 * it loaded) and older loaded ones if they join the page without a gap. Messages inside the
 * page's range that the page lacks were deleted meanwhile.
 */
export function putLatestPage(state: HistoryState, chatId: string, page: MessageDtoCursorPaginatedResponse): void {
  const history = historyOf(state, chatId)
  const items = [...page.items].sort(bySeq)
  const first = items[0]
  const last = items.at(-1)

  if (!first || !last) {
    // No messages at all (or all deleted).
    history.messages = []
    history.hasNewer = false
    history.olderCursor = page.nextCursor
    history.hasOlder = page.hasMore
    return
  }

  const cached = history.messages
  const joins = cached.length > 0 && cached.at(-1)!.seq >= first.seq
  const older = joins ? cached.filter((m) => m.seq < first.seq) : []
  const newer = cached.filter((m) => m.seq > last.seq)

  history.messages = [...older, ...items, ...newer]
  history.hasNewer = false
  if (older.length === 0) {
    history.olderCursor = page.nextCursor
    history.hasOlder = page.hasMore
  }
  dropConfirmedPending(history)
}

/** A page older than what is loaded. */
export function putOlderPage(state: HistoryState, chatId: string, page: MessageDtoCursorPaginatedResponse): void {
  const history = historyOf(state, chatId)
  const known = new Set(history.messages.map((m) => m.id))
  const older = page.items.filter((m) => !known.has(m.id))
  history.messages = [...older, ...history.messages].sort(bySeq)
  history.olderCursor = page.nextCursor
  history.hasOlder = page.hasMore
}

/** A window in the middle of the history (around a message, at a date): replaces what was loaded. */
export function putWindow(state: HistoryState, chatId: string, window: MessageWindowDto): void {
  const history = historyOf(state, chatId)
  history.messages = [...window.items].sort(bySeq)
  history.olderCursor = window.nextCursor
  history.hasOlder = window.hasMore
  history.hasNewer = window.hasNewer
}

/** The next page after the loaded window. */
export function putNewerPage(state: HistoryState, chatId: string, page: MessageWindowDto): void {
  const history = historyOf(state, chatId)
  const known = new Set(history.messages.map((m) => m.id))
  history.messages = [...history.messages, ...page.items.filter((m) => !known.has(m.id))].sort(bySeq)
  history.hasNewer = page.hasNewer
  dropConfirmedPending(history)
}

export function addPending(state: HistoryState, pending: PendingMessage): void {
  historyOf(state, pending.chatId).pending.push(pending)
}

export function updatePending(
  state: HistoryState,
  chatId: string,
  clientMessageId: string,
  change: Partial<Pick<PendingMessage, 'state' | 'error'>>,
): void {
  const pending = state.byChat[chatId]?.pending.find((p) => p.clientMessageId === clientMessageId)
  if (pending) Object.assign(pending, change)
}

export function removePending(state: HistoryState, chatId: string, clientMessageId: string): void {
  const history = state.byChat[chatId]
  if (history) history.pending = history.pending.filter((p) => p.clientMessageId !== clientMessageId)
}

function dropConfirmedPending(history: ChatHistory): void {
  if (history.pending.length === 0) return
  const confirmed = new Set(history.messages.map((m) => m.clientMessageId).filter(Boolean))
  history.pending = history.pending.filter((p) => !confirmed.has(p.clientMessageId))
}

/** A message the server stored: from an event or the answer to our own send. */
export function messageStored(state: HistoryState, message: MessageDto): void {
  const history = state.byChat[message.chatId]
  if (!history) return

  if (message.clientMessageId) {
    history.pending = history.pending.filter((p) => p.clientMessageId !== message.clientMessageId)
  }

  const index = history.messages.findIndex((m) => m.id === message.id)
  if (index !== -1) {
    history.messages[index] = merge(history.messages[index]!, message)
    return
  }

  // Outside the loaded part: it belongs to a page not loaded yet (older, or newer past a gap).
  const first = history.messages[0]
  const last = history.messages.at(-1)
  if (first && message.seq < first.seq && history.hasOlder) return
  if (last && message.seq > last.seq && history.hasNewer) return

  history.messages.push(message)
  history.messages.sort(bySeq)
}

/** Events carry no per-viewer fields (myReaction, status, isRead): keep the ones we know. */
function merge(known: MessageDto, incoming: MessageDto): MessageDto {
  return {
    ...incoming,
    myReaction: incoming.myReaction ?? known.myReaction,
    status: incoming.status ?? known.status,
    isRead: incoming.isRead || known.isRead,
  }
}

export function applyHistoryEvent<K extends keyof JournaledEvents>(
  state: HistoryState,
  type: K,
  payload: JournaledEvents[K],
  ctx: HistoryContext,
): void {
  switch (type) {
    case 'MessageCreated':
      messageStored(state, payload as MessageDto)
      return

    case 'MessageUpdated': {
      const message = payload as MessageDto
      const history = state.byChat[message.chatId]
      if (!history) return
      history.messages = history.messages.map((m) => {
        if (m.id === message.id) return merge(m, message)
        if (m.replyTo?.messageId === message.id) return { ...m, replyTo: { ...m.replyTo, text: message.text } }
        return m
      })
      return
    }

    case 'MessageDeleted': {
      const deleted = payload as JournaledEvents['MessageDeleted']
      const history = state.byChat[deleted.chatId]
      if (!history) return
      history.messages = history.messages
        .filter((m) => m.id !== deleted.messageId)
        .map((m) =>
          deleted.forEveryone && m.replyTo?.messageId === deleted.messageId
            ? { ...m, replyTo: { ...m.replyTo, text: '', deleted: true } }
            : m,
        )
      return
    }

    case 'ReactionsChanged': {
      const change = payload as JournaledEvents['ReactionsChanged']
      const message = state.byChat[change.chatId]?.messages.find((m) => m.id === change.messageId)
      if (!message) return
      message.reactions = change.reactions
      if (change.userId === ctx.meId) message.myReaction = change.emoji
      return
    }

    case 'UserUpdated': {
      const user = payload as JournaledEvents['UserUpdated']
      for (const history of Object.values(state.byChat)) {
        for (const m of history.messages) {
          if (m.senderId === user.userId) m.senderName = user.displayName
        }
      }
      return
    }

    case 'ChatDeleted':
      delete state.byChat[(payload as JournaledEvents['ChatDeleted']).chatId]
      return

    case 'MemberRemoved': {
      const removed = payload as JournaledEvents['MemberRemoved']
      if (removed.userId === ctx.meId) delete state.byChat[removed.chatId]
      return
    }

    default:
      return
  }
}
