import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { useChatsStore } from '@/entities/chat/model/chats.store'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { ME, chat, message } from '@/testing/fixtures'
import * as messagesApi from '../api/messages.api'
import * as messageEntityApi from '@/entities/message/api'
import { useMessagesStore } from './messages.store'

vi.mock('../api/messages.api', () => ({
  markRead: vi.fn(async () => {}),
  sendMessage: vi.fn(),
  sendTyping: vi.fn(async () => {}),
}))
vi.mock('@/entities/message/api', () => ({ getMessagesPage: vi.fn(), PAGE_SIZE: 30 }))

const m1 = message({ seq: 1 })

async function openChatWithUnread() {
  const auth = useAuthStore()
  auth.user = { userId: ME, username: 'me', email: 'me@test', displayName: 'Me', avatarId: null }
  const chats = useChatsStore()
  chats.replaceAll([chat({ lastMessage: m1, lastReadSeq: 0, unreadCount: 1 })], [])
  vi.mocked(messageEntityApi.getMessagesPage).mockResolvedValue({ items: [m1], nextCursor: null, hasMore: false })

  const store = useMessagesStore()
  await store.openChat('chat-1')
  return { store, chats }
}

beforeEach(() => {
  setActivePinia(createPinia())
  vi.useFakeTimers()
  vi.mocked(messagesApi.markRead).mockClear()
})
afterEach(() => vi.useRealTimers())

describe('reading the open chat', () => {
  it('opening the chat reads it', async () => {
    const { chats } = await openChatWithUnread()

    expect(messagesApi.markRead).toHaveBeenCalledWith('chat-1', m1.id)
    expect(chats.get('chat-1')!.unreadCount).toBe(0)
  })

  it('a message that arrives while the chat is on screen is read too', async () => {
    // The bug: only opening the chat sent /read, so after F5 the chat showed it as unread.
    const { store, chats } = await openChatWithUnread()
    const m2 = message({ seq: 2 })
    chats.apply('MessageCreated', m2, { meId: ME })
    const { useHistoryStore } = await import('@/entities/message/model/history.store')
    useHistoryStore().apply('MessageCreated', m2, { meId: ME })

    store.seen()
    await vi.advanceTimersByTimeAsync(1_000)

    expect(messagesApi.markRead).toHaveBeenLastCalledWith('chat-1', m2.id)
    expect(chats.get('chat-1')!.unreadCount).toBe(0)
  })

  it('nothing new: no request', async () => {
    const { store } = await openChatWithUnread()
    vi.mocked(messagesApi.markRead).mockClear()

    store.seen()
    await vi.advanceTimersByTimeAsync(1_000)

    expect(messagesApi.markRead).not.toHaveBeenCalled()
  })
})
