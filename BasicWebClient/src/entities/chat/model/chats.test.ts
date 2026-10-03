import { describe, expect, it } from 'vitest'

import { BOB, ME, chat, message } from '@/testing/fixtures'
import type { JournaledEventName, JournaledEvents } from '@/shared/api/hub.types'
import { applyChatEvent, fromSnapshot, previewArrived, sortedChats, type ChatsEffects, type ChatsState } from './chats'

const ctx = { meId: ME }

function apply<K extends JournaledEventName>(state: ChatsState, type: K, payload: JournaledEvents[K]) {
  const effects: ChatsEffects = { fetchChats: [] }
  applyChatEvent(state, type, payload, ctx, effects)
  return effects
}

const readState = (lastReadSeq: number, unreadCount: number, unreadReactionCount = 0) => ({
  chatId: 'chat-1',
  lastReadSeq,
  unreadCount,
  unreadMentionCount: 0,
  unreadReactionCount,
  markedUnread: false,
})

describe('messages and unread counters', () => {
  it('counts a new message from someone else once, even if it comes twice', () => {
    const state = fromSnapshot([chat({ lastMessage: message({ seq: 1 }), lastReadSeq: 1 })])
    const m2 = message({ seq: 2 })

    apply(state, 'MessageCreated', m2)
    apply(state, 'MessageCreated', m2)

    expect(state.byId['chat-1']!.unreadCount).toBe(1)
    expect(state.byId['chat-1']!.lastMessage!.id).toBe(m2.id)
  })

  it('does not count what the snapshot already counted', () => {
    const m3 = message({ seq: 3 })
    const state = fromSnapshot([chat({ lastMessage: m3, lastReadSeq: 1, unreadCount: 2 })])

    apply(state, 'MessageCreated', m3)

    expect(state.byId['chat-1']!.unreadCount).toBe(2)
  })

  it('does not count own messages', () => {
    const state = fromSnapshot([chat()])

    apply(state, 'MessageCreated', message({ seq: 1, senderId: ME }))

    expect(state.byId['chat-1']!.unreadCount).toBe(0)
    expect(state.byId['chat-1']!.lastMessage!.seq).toBe(1)
  })

  it('counts mentions of me', () => {
    const state = fromSnapshot([chat()])

    apply(state, 'MessageCreated', message({
      seq: 1,
      text: '@me hi',
      entities: [{ type: 'mention', offset: 0, length: 3, userId: ME, url: null, language: null }],
    }))

    expect(state.byId['chat-1']!.unreadMentionCount).toBe(1)
  })

  it('a ChatListUpdated preview moves the row; the counters and the full message come from the journal', () => {
    // The preview is cut and carries no formatting: counted from it, a mention past its end was
    // lost for good, and the full message, with the same seq, never replaced the cut text.
    const state = fromSnapshot([chat({ lastMessage: message({ seq: 1 }), lastReadSeq: 1 })])
    const full = message({
      seq: 2,
      text: '@me hi',
      entities: [{ type: 'mention', offset: 0, length: 3, userId: ME, url: null, language: null }],
    })
    const effects: ChatsEffects = { fetchChats: [] }

    previewArrived(state, { ...full, text: '@me…', entities: [] }, effects)
    expect(state.byId['chat-1']!.lastMessage!.text).toBe('@me…')
    expect(state.byId['chat-1']!.unreadCount).toBe(0)

    apply(state, 'MessageCreated', full)
    expect(state.byId['chat-1']!.lastMessage!.text).toBe('@me hi')
    expect(state.byId['chat-1']!.unreadCount).toBe(1)
    expect(state.byId['chat-1']!.unreadMentionCount).toBe(1)
  })

  it('keeps the newest preview when an older message comes late', () => {
    const state = fromSnapshot([chat()])
    const m5 = message({ seq: 5 })

    apply(state, 'MessageCreated', m5)
    apply(state, 'MessageCreated', message({ seq: 4 }))

    expect(state.byId['chat-1']!.lastMessage!.id).toBe(m5.id)
  })

  it('a catch-up replaying the journal over live events ends with the server count', () => {
    // Journal: m2 (pts 1), read up to 2 on another device (pts 2), m3 (pts 3).
    // Live, this device saw m2, the read and m3 already; the catch-up replays all three.
    const state = fromSnapshot([chat({ lastMessage: message({ seq: 1 }), lastReadSeq: 1 })])
    const m2 = message({ seq: 2 })
    const m3 = message({ seq: 3 })

    apply(state, 'MessageCreated', m2)
    apply(state, 'ReadStateChanged', readState(2, 0))
    apply(state, 'MessageCreated', m3)
    expect(state.byId['chat-1']!.unreadCount).toBe(1)

    apply(state, 'MessageCreated', m2)
    apply(state, 'ReadStateChanged', readState(2, 0))
    apply(state, 'MessageCreated', m3)
    expect(state.byId['chat-1']!.unreadCount).toBe(1)
  })

  it('new reactions to my messages come with the counters, without moving the row', () => {
    const state = fromSnapshot([chat({ lastMessage: message({ seq: 3 }), lastReadSeq: 3 })])

    apply(state, 'ReadStateChanged', readState(3, 0, 2))

    expect(state.byId['chat-1']!.unreadReactionCount).toBe(2)
    expect(state.byId['chat-1']!.unreadCount).toBe(0)
  })

  it('a catch-up of events missed while offline adds them', () => {
    const state = fromSnapshot([chat({ lastMessage: message({ seq: 1 }), lastReadSeq: 1 })])

    apply(state, 'MessageCreated', message({ seq: 2 }))
    apply(state, 'MessageCreated', message({ seq: 3 }))

    expect(state.byId['chat-1']!.unreadCount).toBe(2)
  })

  it('asks for the row of an unknown chat', () => {
    const state = fromSnapshot([])

    const effects = apply(state, 'MessageCreated', message({ chatId: 'chat-9', seq: 1 }))

    expect(effects.fetchChats).toEqual(['chat-9'])
  })

  it('asks for the row when the last message is deleted', () => {
    const m2 = message({ seq: 2 })
    const state = fromSnapshot([chat({ lastMessage: m2, lastReadSeq: 2 })])

    const effects = apply(state, 'MessageDeleted', { chatId: 'chat-1', messageId: m2.id, seq: 2, forEveryone: true })

    expect(effects.fetchChats).toEqual(['chat-1'])
  })

  it('an edit of the last message updates the preview', () => {
    const m2 = message({ seq: 2 })
    const state = fromSnapshot([chat({ lastMessage: m2 })])

    apply(state, 'MessageUpdated', { ...m2, text: 'edited', editedAt: m2.createdAt })

    expect(state.byId['chat-1']!.lastMessage!.text).toBe('edited')
  })
})

describe('chat state', () => {
  it('receipts only move forward; read implies delivered', () => {
    const state = fromSnapshot([chat({ outboxDeliveredSeq: 5 })])

    apply(state, 'MessagesDelivered', { chatId: 'chat-1', userId: BOB, seq: 3 })
    apply(state, 'MessagesRead', { chatId: 'chat-1', userId: BOB, seq: 7 })

    expect(state.byId['chat-1']!.outboxDeliveredSeq).toBe(7)
    expect(state.byId['chat-1']!.outboxReadSeq).toBe(7)
  })

  it('removes the chat when I am removed from it, not when someone else is', () => {
    const state = fromSnapshot([chat({ type: 'group' })])

    apply(state, 'MemberRemoved', { chatId: 'chat-1', userId: BOB, removedBy: ME })
    expect(state.byId['chat-1']).toBeDefined()

    apply(state, 'MemberRemoved', { chatId: 'chat-1', userId: ME, removedBy: BOB })
    expect(state.byId['chat-1']).toBeUndefined()
  })

  it('a renamed companion is renamed in the list', () => {
    const state = fromSnapshot([chat()])

    apply(state, 'UserUpdated', { userId: BOB, displayName: 'Robert', username: 'bob', avatarId: 'av-1' })

    expect(state.byId['chat-1']!.companionName).toBe('Robert')
    expect(state.byId['chat-1']!.avatarId).toBe('av-1')
  })

  it('sorts pinned chats first in their order, then by activity', () => {
    const at = (minute: number) => new Date(Date.UTC(2026, 9, 1, 12, minute)).toISOString()
    const state = fromSnapshot([
      chat({ chatId: 'old', lastActivityAt: at(1) }),
      chat({ chatId: 'new', lastActivityAt: at(5) }),
      chat({ chatId: 'pin-b', lastActivityAt: at(0) }),
      chat({ chatId: 'pin-a', lastActivityAt: at(0) }),
    ])

    apply(state, 'PinnedChatsChanged', { chatIds: ['pin-a', 'pin-b'] })

    expect(sortedChats(state).map((c) => c.chatId)).toEqual(['pin-a', 'pin-b', 'new', 'old'])
  })

  it('pinned positions count from 1, as the server sends them', () => {
    // The snapshot has 1-based positions; an event that set 0-based ones would mix the two.
    const state = fromSnapshot([chat({ chatId: 'a', pinnedPosition: 1 }), chat({ chatId: 'b' })])

    apply(state, 'PinnedChatsChanged', { chatIds: ['b', 'a'] })

    expect(state.byId['b']!.pinnedPosition).toBe(1)
    expect(state.byId['a']!.pinnedPosition).toBe(2)
  })
})
