import { describe, expect, it } from 'vitest'

import { BOB, ME, message } from '@/testing/fixtures'
import type { MessageDto } from '@/shared/api/schema'
import {
  addPending,
  applyHistoryEvent,
  emptyHistory,
  messageStored,
  putLatestPage,
  putOlderPage,
  type HistoryState,
} from './history'

const ctx = { meId: ME }
const page = (items: MessageDto[], hasMore = false, nextCursor: string | null = null) => ({ items, hasMore, nextCursor })
const seqs = (state: HistoryState) => state.byChat['chat-1']!.messages.map((m) => m.seq)

describe('pages', () => {
  it('older pages go before, without duplicates', () => {
    const state = emptyHistory()
    const m3 = message({ seq: 3 })
    putLatestPage(state, 'chat-1', page([m3, message({ seq: 4 })], true, 'c1'))
    putOlderPage(state, 'chat-1', page([message({ seq: 1 }), message({ seq: 2 }), m3], false))

    expect(seqs(state)).toEqual([1, 2, 3, 4])
    expect(state.byChat['chat-1']!.hasOlder).toBe(false)
  })

  it('a reloaded latest page keeps live arrivals and joined older messages, drops deleted ones', () => {
    const state = emptyHistory()
    putLatestPage(state, 'chat-1', page([message({ seq: 1 }), message({ seq: 2 }), message({ seq: 3 })]))
    messageStored(state, message({ seq: 5 }))

    // Reload: seq 3 was deleted meanwhile; the page covers 2..4.
    putLatestPage(state, 'chat-1', page([message({ seq: 2 }), message({ seq: 4 })], true, 'c'))

    expect(seqs(state)).toEqual([1, 2, 4, 5])
  })

  it('a latest page with a gap to the cache replaces it', () => {
    const state = emptyHistory()
    putLatestPage(state, 'chat-1', page([message({ seq: 1 })]))
    putLatestPage(state, 'chat-1', page([message({ seq: 80 })], true, 'c80'))

    expect(seqs(state)).toEqual([80])
    expect(state.byChat['chat-1']!.olderCursor).toBe('c80')
  })
})

describe('events', () => {
  it('a message stored twice is kept once, in seq order', () => {
    const state = emptyHistory()
    putLatestPage(state, 'chat-1', page([message({ seq: 1 })]))
    const m3 = message({ seq: 3 })
    const m2 = message({ seq: 2 })

    applyHistoryEvent(state, 'MessageCreated', m3, ctx)
    applyHistoryEvent(state, 'MessageCreated', m2, ctx)
    applyHistoryEvent(state, 'MessageCreated', m3, ctx)

    expect(seqs(state)).toEqual([1, 2, 3])
  })

  it('ignores chats that are not loaded', () => {
    const state = emptyHistory()
    applyHistoryEvent(state, 'MessageCreated', message({ chatId: 'chat-2' }), ctx)
    expect(state.byChat['chat-2']).toBeUndefined()
  })

  it('the stored message replaces its pending copy', () => {
    const state = emptyHistory()
    putLatestPage(state, 'chat-1', page([]))
    addPending(state, {
      clientMessageId: 'cm-1', chatId: 'chat-1', text: 'hi', entities: [], replyToMessageId: null,
      createdAt: new Date().toISOString(), state: 'sending', error: null,
    })

    applyHistoryEvent(state, 'MessageCreated', message({ seq: 1, senderId: ME, clientMessageId: 'cm-1' }), ctx)

    expect(state.byChat['chat-1']!.pending).toEqual([])
    expect(seqs(state)).toEqual([1])
  })

  it('an edit keeps my reaction, which events do not carry', () => {
    const state = emptyHistory()
    const m1 = message({ seq: 1, myReaction: '🔥' })
    putLatestPage(state, 'chat-1', page([m1]))

    applyHistoryEvent(state, 'MessageUpdated', { ...m1, text: 'edited', myReaction: null }, ctx)

    const stored = state.byChat['chat-1']!.messages[0]!
    expect(stored.text).toBe('edited')
    expect(stored.myReaction).toBe('🔥')
  })

  it('a deletion removes the message and marks replies to it', () => {
    const state = emptyHistory()
    const m1 = message({ seq: 1 })
    const reply = message({
      seq: 2,
      replyTo: { messageId: m1.id, senderId: BOB, senderName: 'Bob', text: m1.text, deleted: false },
    })
    putLatestPage(state, 'chat-1', page([m1, reply]))

    applyHistoryEvent(state, 'MessageDeleted', { chatId: 'chat-1', messageId: m1.id, seq: 1, forEveryone: true }, ctx)

    expect(seqs(state)).toEqual([2])
    expect(state.byChat['chat-1']!.messages[0]!.replyTo!.deleted).toBe(true)
  })

  it('reactions: counts for everyone, my own only from my events', () => {
    const state = emptyHistory()
    const m1 = message({ seq: 1 })
    putLatestPage(state, 'chat-1', page([m1]))
    const reactions = [{ emoji: '👍', count: 1 }]

    applyHistoryEvent(state, 'ReactionsChanged', { chatId: 'chat-1', messageId: m1.id, userId: BOB, emoji: '👍', reactions }, ctx)
    expect(state.byChat['chat-1']!.messages[0]!.myReaction).toBeNull()

    applyHistoryEvent(state, 'ReactionsChanged', { chatId: 'chat-1', messageId: m1.id, userId: ME, emoji: '👍', reactions }, ctx)
    expect(state.byChat['chat-1']!.messages[0]!.myReaction).toBe('👍')
    expect(state.byChat['chat-1']!.messages[0]!.reactions).toEqual(reactions)
  })
})
