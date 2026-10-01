// The chat list as a pure state machine: snapshot + events -> state. No Vue, no I/O.
//
// Every event may arrive twice (live from the hub and again from the journal on catch-up), and a
// catch-up replays the journal in order on top of what was already applied live. So events either
// replace state (last write wins) or are guarded:
//  - lastMessage moves only forward by seq;
//  - unread counters grow only for seq > countedUpTo; a snapshot or ReadStateChanged replaces the
//    counters and resets countedUpTo. A replay re-applies that replace and then the messages after
//    it, so the result converges to the server's.

import type { ChatListItem } from '../types'
import type { MessageDto } from '@/shared/api/schema'
import type { JournaledEvents } from '@/shared/api/hub.types'

export interface ChatsState {
  byId: Record<string, ChatListItem>
  /** chatId -> the highest seq already reflected in unreadCount. */
  countedUpTo: Record<string, number>
}

export interface ChatsContext {
  meId: string
}

/** Follow-up work a reducer cannot do itself: rows to reload from the server. */
export interface ChatsEffects {
  fetchChats: string[]
}

export function emptyChats(): ChatsState {
  return { byId: {}, countedUpTo: {} }
}

const baseline = (chat: ChatListItem) => Math.max(chat.lastReadSeq, chat.lastMessage?.seq ?? 0)

/** Replace one row with the server's version (snapshot, ChatCreated, a reloaded row). */
export function putChat(state: ChatsState, chat: ChatListItem): void {
  state.byId[chat.chatId] = chat
  state.countedUpTo[chat.chatId] = baseline(chat)
}

export function removeChat(state: ChatsState, chatId: string): void {
  delete state.byId[chatId]
  delete state.countedUpTo[chatId]
}

export function fromSnapshot(chats: ChatListItem[]): ChatsState {
  const state = emptyChats()
  for (const chat of chats) putChat(state, chat)
  return state
}

const mentions = (message: MessageDto, meId: string) =>
  message.entities.some((e) => e.type === 'mention' && e.userId === meId)

/** A new message in a chat: MessageCreated, or the ChatListUpdated preview of it. */
export function messageArrived(
  state: ChatsState,
  message: MessageDto,
  ctx: ChatsContext,
  effects: ChatsEffects,
): void {
  const chat = state.byId[message.chatId]
  if (!chat) {
    effects.fetchChats.push(message.chatId)
    return
  }

  if (message.seq > (chat.lastMessage?.seq ?? 0)) {
    chat.lastMessage = message
    chat.lastActivityAt = message.createdAt
  }

  const counted = state.countedUpTo[chat.chatId] ?? 0
  if (message.seq <= counted) return
  state.countedUpTo[chat.chatId] = message.seq

  // Own messages never count as unread (and sending does not move the read pointer).
  if (message.senderId !== ctx.meId && message.seq > chat.lastReadSeq) {
    chat.unreadCount += 1
    if (mentions(message, ctx.meId)) chat.unreadMentionCount += 1
  }
}

/** Applies one journaled event. Unknown chats are left alone unless a row must appear. */
export function applyChatEvent<K extends keyof JournaledEvents>(
  state: ChatsState,
  type: K,
  payload: JournaledEvents[K],
  ctx: ChatsContext,
  effects: ChatsEffects,
): void {
  switch (type) {
    case 'MessageCreated':
      messageArrived(state, payload as MessageDto, ctx, effects)
      return

    case 'MessageUpdated': {
      const message = payload as JournaledEvents['MessageUpdated']
      const chat = state.byId[message.chatId]
      if (chat?.lastMessage?.id === message.id) chat.lastMessage = { ...chat.lastMessage, ...message }
      return
    }

    case 'MessageDeleted': {
      // The new preview and the unread count after a deletion are the server's to compute.
      const deleted = payload as JournaledEvents['MessageDeleted']
      const chat = state.byId[deleted.chatId]
      if (chat && (chat.lastMessage?.id === deleted.messageId || deleted.seq > chat.lastReadSeq)) {
        effects.fetchChats.push(deleted.chatId)
      }
      return
    }

    case 'ChatCreated':
      putChat(state, payload as ChatListItem)
      return

    case 'ChatUpdated': {
      const update = payload as JournaledEvents['ChatUpdated']
      const chat = state.byId[update.chatId]
      if (chat) {
        chat.title = update.title
        chat.avatarId = update.avatarId
      }
      return
    }

    case 'ChatDeleted':
      removeChat(state, (payload as JournaledEvents['ChatDeleted']).chatId)
      return

    case 'MemberRemoved': {
      const removed = payload as JournaledEvents['MemberRemoved']
      if (removed.userId === ctx.meId) removeChat(state, removed.chatId)
      return
    }

    case 'ReadStateChanged': {
      const read = payload as JournaledEvents['ReadStateChanged']
      const chat = state.byId[read.chatId]
      if (!chat) return
      chat.lastReadSeq = read.lastReadSeq
      chat.unreadCount = read.unreadCount
      chat.unreadMentionCount = read.unreadMentionCount
      chat.markedUnread = read.markedUnread
      state.countedUpTo[read.chatId] = read.lastReadSeq
      return
    }

    case 'DraftUpdated': {
      const draft = payload as JournaledEvents['DraftUpdated']
      const chat = state.byId[draft.chatId]
      if (chat) chat.draft = draft.draft
      return
    }

    case 'MessagesDelivered': {
      const receipt = payload as JournaledEvents['MessagesDelivered']
      const chat = state.byId[receipt.chatId]
      if (chat) chat.outboxDeliveredSeq = Math.max(chat.outboxDeliveredSeq, receipt.seq)
      return
    }

    case 'MessagesRead': {
      // Read implies delivered.
      const receipt = payload as JournaledEvents['MessagesRead']
      const chat = state.byId[receipt.chatId]
      if (!chat) return
      chat.outboxReadSeq = Math.max(chat.outboxReadSeq, receipt.seq)
      chat.outboxDeliveredSeq = Math.max(chat.outboxDeliveredSeq, receipt.seq)
      return
    }

    case 'PinnedChatsChanged': {
      const order = (payload as JournaledEvents['PinnedChatsChanged']).chatIds
      for (const chat of Object.values(state.byId)) {
        const index = order.indexOf(chat.chatId)
        chat.pinnedPosition = index === -1 ? null : index + 1
      }
      return
    }

    case 'ChatStateChanged': {
      const change = payload as JournaledEvents['ChatStateChanged']
      const chat = state.byId[change.chatId]
      if (!chat) return
      chat.archived = change.archived
      chat.isMuted = change.isMuted
      chat.mutedUntil = change.mutedUntil
      return
    }

    case 'UserUpdated': {
      const user = payload as JournaledEvents['UserUpdated']
      for (const chat of Object.values(state.byId)) {
        if (chat.companionId !== user.userId) continue
        chat.companionName = user.displayName
        chat.companionUsername = user.username
        chat.avatarId = user.avatarId
      }
      return
    }

    default:
      // Members, folders, privacy, blocks and reactions do not change the list rows.
      return
  }
}

/** Pinned first in their order, then the most recently active. */
export function sortedChats(state: ChatsState): ChatListItem[] {
  return Object.values(state.byId).sort((a, b) => {
    const pa = a.pinnedPosition ?? Number.POSITIVE_INFINITY
    const pb = b.pinnedPosition ?? Number.POSITIVE_INFINITY
    if (pa !== pb) return pa - pb
    return Date.parse(b.lastActivityAt) - Date.parse(a.lastActivityAt)
  })
}
