import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { useChatsStore } from '@/entities/chat/model/chats.store'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { ME, chat, message } from '@/testing/fixtures'
import * as messagesApi from '../api/messages.api'
import * as messageEntityApi from '@/entities/message/api'
import { ApiError } from '@/shared/api/problem'
import { useNoticesStore } from '@/shared/ui/notices.store'
import { useMessagesStore } from './messages.store'

vi.mock('../api/messages.api', () => ({
  markRead: vi.fn(async () => {}),
  editMessage: vi.fn(),
  forwardMessages: vi.fn(),
  setReaction: vi.fn(),
  removeReaction: vi.fn(async () => {}),
  deleteMessage: vi.fn(async () => {}),
  sendMessage: vi.fn(),
  sendTyping: vi.fn(async () => {}),
  saveDraft: vi.fn(async () => null),
  removeDraft: vi.fn(async () => {}),
}))
vi.mock('@/entities/message/api', () => ({
  getMessagesPage: vi.fn(),
  getMessageContext: vi.fn(),
  getMessagesAfter: vi.fn(),
  getMessagesAt: vi.fn(),
  PAGE_SIZE: 30,
}))
vi.mock('@/entities/chat/api', () => ({ getChatItem: vi.fn(() => new Promise(() => {})) }))

const m1 = message({ seq: 1 })

async function openChatWithUnread() {
  const auth = useAuthStore()
  auth.user = { userId: ME, username: 'me', email: 'me@test', displayName: 'Me', avatarId: null }
  const chats = useChatsStore()
  chats.replaceAll([chat({ lastMessage: m1, lastReadSeq: 0, unreadCount: 1 })], [])
  // A copy per test: the reducers update loaded messages in place.
  vi.mocked(messageEntityApi.getMessagesPage).mockResolvedValue({ items: [{ ...m1 }], nextCursor: null, hasMore: false })

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

describe('a failed load of the open chat', () => {
  it('is retried by itself until it loads', async () => {
    // Seen with a database restart: the chat said "could not load" until another chat was opened.
    const auth = useAuthStore()
    auth.user = { userId: ME, username: 'me', email: 'me@test', displayName: 'Me', avatarId: null }
    useChatsStore().replaceAll([chat({ lastMessage: m1 })], [])
    vi.mocked(messageEntityApi.getMessagesPage)
      .mockRejectedValueOnce(new ApiError(503, { errorCode: 'SERVICE_UNAVAILABLE' }))
      .mockRejectedValueOnce(new ApiError(500, { errorCode: 'INTERNAL_ERROR' }))
      .mockResolvedValue({ items: [{ ...m1 }], nextCursor: null, hasMore: false })
    const store = useMessagesStore()

    await store.openChat('chat-1')
    expect(store.error).toBe('Не удалось загрузить сообщения')

    await vi.advanceTimersByTimeAsync(30_000)

    expect(store.error).toBe('')
    expect(store.messages.map((m) => m.id)).toEqual([m1.id])
  })

  it('stops retrying once another chat is open', async () => {
    const auth = useAuthStore()
    auth.user = { userId: ME, username: 'me', email: 'me@test', displayName: 'Me', avatarId: null }
    useChatsStore().replaceAll([chat({ lastMessage: m1 }), chat({ chatId: 'chat-2' })], [])
    vi.mocked(messageEntityApi.getMessagesPage).mockReset()
    vi.mocked(messageEntityApi.getMessagesPage).mockRejectedValue(new ApiError(500, { errorCode: 'INTERNAL_ERROR' }))
    const store = useMessagesStore()

    await store.openChat('chat-1')
    store.reset()
    const calls = vi.mocked(messageEntityApi.getMessagesPage).mock.calls.length
    await vi.advanceTimersByTimeAsync(120_000)

    expect(vi.mocked(messageEntityApi.getMessagesPage).mock.calls.length).toBe(calls)
  })
})

describe('reading the open chat', () => {
  it('loading the chat does not read it: the list does, once someone sees it', async () => {
    // The bug: every load marked the chat read — in a hidden tab, after a reconnect, scrolled up.
    const { store, chats } = await openChatWithUnread()
    expect(messagesApi.markRead).not.toHaveBeenCalled()
    expect(store.latestVersion).toBe(1)

    store.seen()
    await vi.advanceTimersByTimeAsync(1_000)

    expect(messagesApi.markRead).toHaveBeenCalledWith('chat-1', m1.id)
    expect(chats.get('chat-1')!.unreadCount).toBe(0)
  })

  it('seen, then another chat opened at once: the first chat is still read', async () => {
    // The bug: the pause before /read took whichever chat was open when it ended — a chat looked
    // at and left within half a second stayed unread (found by the UI e2e).
    const { store, chats } = await openChatWithUnread()
    chats.put(chat({ chatId: 'chat-2' }))
    vi.mocked(messageEntityApi.getMessagesPage).mockResolvedValue({ items: [], nextCursor: null, hasMore: false })

    store.seen()
    await store.openChat('chat-2')
    await vi.advanceTimersByTimeAsync(1_000)

    expect(messagesApi.markRead).toHaveBeenCalledWith('chat-1', m1.id)
  })

  it('a read mark that did not reach the server is sent again', async () => {
    // The bug: the counters dropped before the request, so after a failure nothing looked unread
    // and the mark was never sent — other devices and the sender kept "unread".
    const { store } = await openChatWithUnread()
    vi.mocked(messagesApi.markRead).mockRejectedValueOnce(new Error('offline'))

    store.seen()
    await vi.advanceTimersByTimeAsync(1_000)
    await vi.advanceTimersByTimeAsync(5_000)

    expect(messagesApi.markRead).toHaveBeenCalledTimes(2)
    expect(messagesApi.markRead).toHaveBeenLastCalledWith('chat-1', m1.id)
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
    store.seen()
    await vi.advanceTimersByTimeAsync(1_000)
    vi.mocked(messagesApi.markRead).mockClear()

    store.seen()
    await vi.advanceTimersByTimeAsync(1_000)

    expect(messagesApi.markRead).not.toHaveBeenCalled()
  })
})

describe('edit and delete', () => {
  it('an edit applies the server answer to the history and the list preview', async () => {
    const { store, chats } = await openChatWithUnread()
    vi.mocked(messagesApi.editMessage).mockResolvedValue({ ...m1, text: 'fixed', editedAt: m1.createdAt })

    store.startEdit(m1)
    expect(await store.saveEdit('fixed')).toBe(true)

    expect(store.messages[0]!.text).toBe('fixed')
    expect(chats.get('chat-1')!.lastMessage!.text).toBe('fixed')
    expect(store.editing).toBeNull()
  })

  it('a refused edit keeps the editor open and tells why', async () => {
    const { store } = await openChatWithUnread()
    vi.mocked(messagesApi.editMessage).mockRejectedValue(new ApiError(403, { errorCode: 'EDIT_WINDOW_EXPIRED' }))

    store.startEdit(m1)
    expect(await store.saveEdit('late')).toBe(false)

    expect(store.editing).not.toBeNull()
    expect(useNoticesStore().items[0]!.text).toContain('нельзя изменить')
  })

  it('a deletion removes the message at once', async () => {
    const { store } = await openChatWithUnread()

    await store.remove(m1, true)

    expect(messagesApi.deleteMessage).toHaveBeenCalledWith('chat-1', m1.id, true)
    expect(store.messages).toEqual([])
  })
})

describe('reply and forward', () => {
  it('a reply carries replyToMessageId and the reply context is cleared', async () => {
    const { store } = await openChatWithUnread()
    vi.mocked(messagesApi.sendMessage).mockReturnValue(new Promise(() => {}))

    store.startReply(m1)
    store.send('answer')

    expect(store.replyTo).toBeNull()
    await vi.waitFor(() =>
      expect(messagesApi.sendMessage).toHaveBeenCalledWith('chat-1', expect.objectContaining({ replyToMessageId: m1.id })),
    )
  })

  it('forwards the selected messages in the chat order with a clientMessageId each', async () => {
    const { store } = await openChatWithUnread()
    const m2 = message({ seq: 2 })
    const { useHistoryStore } = await import('@/entities/message/model/history.store')
    useHistoryStore().apply('MessageCreated', m2, { meId: ME })
    const copy = (m: typeof m1, seq: number) => ({ ...m, id: `copy-${seq}`, chatId: 'chat-2', seq, forwardFrom: { senderId: m.senderId, senderName: m.senderName } })
    vi.mocked(messagesApi.forwardMessages).mockResolvedValue({ items: [copy(m1, 1), copy(m2, 2)] })

    store.toggleSelected(m2.id)
    store.toggleSelected(m1.id)
    const count = await store.forward('chat-2', [...store.selected])

    const body = vi.mocked(messagesApi.forwardMessages).mock.calls[0]![1]
    expect(body.messageIds).toEqual([m1.id, m2.id])
    expect(body.clientMessageIds).toHaveLength(2)
    expect(count).toBe(2)
    expect(store.selected.size).toBe(0)
  })
})

describe('reactions', () => {
  it('puts a reaction and shows the summary from the answer', async () => {
    const { store } = await openChatWithUnread()
    vi.mocked(messagesApi.setReaction).mockResolvedValue({
      chatId: 'chat-1', messageId: m1.id, userId: ME, emoji: '🔥', reactions: [{ emoji: '🔥', count: 1 }],
    })

    await store.react(store.messages[0]!, '🔥')

    expect(store.messages[0]!.myReaction).toBe('🔥')
    expect(store.messages[0]!.reactions).toEqual([{ emoji: '🔥', count: 1 }])
  })

  it('the same reaction again takes it off', async () => {
    const { store } = await openChatWithUnread()
    vi.mocked(messagesApi.setReaction).mockResolvedValue({
      chatId: 'chat-1', messageId: m1.id, userId: ME, emoji: '🔥', reactions: [{ emoji: '🔥', count: 2 }],
    })
    await store.react(store.messages[0]!, '🔥')

    await store.react(store.messages[0]!, '🔥')

    expect(messagesApi.removeReaction).toHaveBeenCalledWith('chat-1', m1.id)
    expect(store.messages[0]!.myReaction).toBeNull()
    expect(store.messages[0]!.reactions).toEqual([{ emoji: '🔥', count: 1 }])
  })
})

describe('jumping in the history', () => {
  it('a message sent while the history is open in the middle takes the chat to its newest part', async () => {
    // The bug: the server's answer belongs past the gap, so the sent message vanished from the
    // list until the user scrolled all the way down.
    const { store } = await openChatWithUnread()
    vi.mocked(messageEntityApi.getMessageContext).mockResolvedValue({
      items: [message({ seq: 500, id: 'm500' })], nextCursor: null, hasMore: false, hasNewer: true,
    } as never)
    await store.jumpTo('m500')
    expect(store.hasNewer).toBe(true)
    vi.mocked(messageEntityApi.getMessagesPage).mockClear()
    vi.mocked(messagesApi.sendMessage).mockImplementation(() => new Promise(() => {}))

    store.send('hello')
    await vi.advanceTimersByTimeAsync(0)

    expect(messageEntityApi.getMessagesPage).toHaveBeenCalledWith('chat-1', null, expect.anything())
    expect(store.hasNewer).toBe(false)
  })

  it('a message not loaded opens the history around it, and newer pages follow', async () => {
    const { store } = await openChatWithUnread()
    const far = message({ seq: 500 })
    vi.mocked(messageEntityApi.getMessageContext).mockResolvedValue({
      items: [message({ seq: 499 }), far, message({ seq: 501 })], nextCursor: 'c', hasMore: true, hasNewer: true,
    })
    vi.mocked(messageEntityApi.getMessagesAfter).mockResolvedValue({
      items: [message({ seq: 502 })], nextCursor: null, hasMore: true, hasNewer: false,
    })

    await store.jumpTo(far.id)

    expect(store.jumpTarget).toBe(far.id)
    expect(store.messages.map((m) => m.seq)).toEqual([499, 500, 501])
    expect(store.hasNewer).toBe(true)

    await store.loadNewer()
    expect(messageEntityApi.getMessagesAfter).toHaveBeenCalledWith('chat-1', 501)
    expect(store.messages.map((m) => m.seq)).toEqual([499, 500, 501, 502])
    expect(store.hasNewer).toBe(false)
  })
})

describe('jumping to a date', () => {
  it('a date before the first message keeps the chat as it was', async () => {
    // The bug: the empty answer replaced the history; the chat stayed empty, without "↓".
    const { store } = await openChatWithUnread()
    vi.mocked(messageEntityApi.getMessagesAt).mockResolvedValue({ items: [], nextCursor: null, hasMore: false })

    await store.jumpToDate(new Date('2020-01-01T00:00:00Z'))

    expect(store.messages.map((m) => m.id)).toEqual([m1.id])
    expect(useNoticesStore().items.map((n) => n.text)).toContain('В этот день сообщений ещё не было')
  })
})

describe('opening a chat at a found message', () => {
  it('an error of the chat before does not show in the next one', async () => {
    const { store } = await openChatWithUnread()
    store.error = 'Не удалось загрузить сообщения'
    vi.mocked(messageEntityApi.getMessageContext).mockResolvedValue({ items: [message({ chatId: 'chat-2', seq: 5 })], hasOlder: false, hasNewer: false } as never)
    useChatsStore().put(chat({ chatId: 'chat-2' }))

    store.requestJump('chat-2', 'm-x')
    await store.openChat('chat-2')

    expect(store.error).toBe('')
  })

  it('a requested jump opens the chat around the message instead of its newest page', async () => {
    setActivePinia(createPinia())
    const auth = useAuthStore()
    auth.user = { userId: ME, username: 'me', email: 'me@test', displayName: 'Me', avatarId: null }
    useChatsStore().replaceAll([chat({ chatId: 'chat-2' })], [])
    const found = message({ chatId: 'chat-2', seq: 40 })
    vi.mocked(messageEntityApi.getMessagesPage).mockClear()
    vi.mocked(messageEntityApi.getMessageContext).mockResolvedValue({
      items: [{ ...found }], nextCursor: 'c', hasMore: true, hasNewer: true,
    })
    const store = useMessagesStore()

    store.requestJump('chat-2', found.id)
    await store.openChat('chat-2')

    expect(messageEntityApi.getMessageContext).toHaveBeenCalledWith('chat-2', found.id)
    expect(messageEntityApi.getMessagesPage).not.toHaveBeenCalled()
    expect(store.jumpTarget).toBe(found.id)
  })
})

describe('a companion who limits who may write', () => {
  it('a send refused by their privacy marks the chat; a message that goes through clears it', async () => {
    const { store } = await openChatWithUnread()
    vi.mocked(messagesApi.sendMessage).mockRejectedValueOnce(new ApiError(403, { errorCode: 'PRIVACY_RESTRICTED' }))

    store.send('hello')
    await vi.advanceTimersByTimeAsync(0)
    expect(store.isRestricted('chat-1')).toBe(true)

    store.tryAgain('chat-1')
    expect(store.isRestricted('chat-1')).toBe(false)

    vi.mocked(messagesApi.sendMessage).mockRejectedValueOnce(new ApiError(403, { errorCode: 'PRIVACY_RESTRICTED' }))
    store.send('again')
    await vi.advanceTimersByTimeAsync(0)
    vi.mocked(messagesApi.sendMessage).mockResolvedValueOnce(message({ id: 'm9', seq: 9, senderId: ME }))
    store.send('now it works')
    await vi.advanceTimersByTimeAsync(0)
    expect(store.isRestricted('chat-1')).toBe(false)
  })
})
