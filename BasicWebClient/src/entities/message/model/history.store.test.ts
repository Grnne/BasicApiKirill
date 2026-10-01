import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { ME, message } from '@/testing/fixtures'
import type { MessageDto } from '@/shared/api/schema'
import * as messageApi from '../api'
import { useHistoryStore } from './history.store'

vi.mock('../api', () => ({
  getMessagesPage: vi.fn(),
  getMessageContext: vi.fn(),
  getMessagesAfter: vi.fn(),
  getMessagesAt: vi.fn(),
  PAGE_SIZE: 30,
}))

const ctx = { meId: ME }
const range = (from: number, to: number) => Array.from({ length: to - from + 1 }, (_, i) => message({ seq: from + i, id: `m${from + i}` }))

/** A request the test answers by hand. */
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((r) => (resolve = r))
  return { promise, resolve }
}

const seqs = (messages: readonly MessageDto[] | undefined) => (messages ?? []).map((m) => m.seq)

beforeEach(() => {
  setActivePinia(createPinia())
  vi.mocked(messageApi.getMessagesPage).mockReset()
})

describe('pages that come back after the history moved on', () => {
  it('an older page asked for before a jump is not glued to the window the jump opened', async () => {
    // The bug: seq 970-999 went in front of the window 100-130 and the list jumped across the gap.
    const store = useHistoryStore()
    vi.mocked(messageApi.getMessagesPage).mockResolvedValueOnce({ items: range(1000, 1029), nextCursor: 'c-970', hasMore: true })
    await store.loadLatest('chat-1')
    const older = deferred<Awaited<ReturnType<typeof messageApi.getMessagesPage>>>()
    vi.mocked(messageApi.getMessagesPage).mockReturnValueOnce(older.promise)
    const loading = store.loadOlder('chat-1')

    vi.mocked(messageApi.getMessageContext).mockResolvedValue({ items: range(100, 130), nextCursor: 'c-99', hasMore: true, hasNewer: true })
    await store.loadContext('chat-1', 'm115')
    older.resolve({ items: range(970, 999), nextCursor: 'c-940', hasMore: true })
    await loading

    expect(seqs(store.get('chat-1')?.messages)).toEqual(seqs(range(100, 130)))
    expect(store.get('chat-1')?.olderCursor).toBe('c-99')
  })

  it('a newer page asked for before "back to the latest" does not bring the window back', async () => {
    const store = useHistoryStore()
    vi.mocked(messageApi.getMessageContext).mockResolvedValue({ items: range(100, 130), nextCursor: null, hasMore: false, hasNewer: true })
    await store.loadContext('chat-1', 'm115')
    const newer = deferred<Awaited<ReturnType<typeof messageApi.getMessagesAfter>>>()
    vi.mocked(messageApi.getMessagesAfter).mockReturnValueOnce(newer.promise)
    const loading = store.loadNewer('chat-1')

    vi.mocked(messageApi.getMessagesPage).mockResolvedValueOnce({ items: range(1000, 1029), nextCursor: 'c', hasMore: true })
    await store.loadLatest('chat-1')
    newer.resolve({ items: range(131, 160), nextCursor: null, hasMore: true, hasNewer: true })
    await loading

    expect(store.get('chat-1')?.hasNewer).toBe(false)
    expect(seqs(store.get('chat-1')?.messages)).toEqual(seqs(range(1000, 1029)))
  })
})

describe('events while the first page of a chat loads', () => {
  it('a message that arrives before the page is kept', async () => {
    // The bug: with no history yet the event was dropped, and the page was built before it.
    const store = useHistoryStore()
    const page = deferred<Awaited<ReturnType<typeof messageApi.getMessagesPage>>>()
    vi.mocked(messageApi.getMessagesPage).mockReturnValueOnce(page.promise)
    const loading = store.loadLatest('chat-1')

    store.apply('MessageCreated', message({ seq: 31, id: 'm31' }), ctx)
    page.resolve({ items: range(1, 30), nextCursor: null, hasMore: false })
    await loading

    expect(seqs(store.get('chat-1')?.messages).at(-1)).toBe(31)
  })

  it('an edit that arrives before the page is not undone by it', async () => {
    const store = useHistoryStore()
    const page = deferred<Awaited<ReturnType<typeof messageApi.getMessagesPage>>>()
    vi.mocked(messageApi.getMessagesPage).mockReturnValueOnce(page.promise)
    const loading = store.loadLatest('chat-1')

    store.apply('MessageUpdated', message({ seq: 30, id: 'm30', text: 'edited' }), ctx)
    vi.mocked(messageApi.getMessagesPage).mockResolvedValueOnce({
      items: [...range(1, 29), message({ seq: 30, id: 'm30', text: 'edited' })], nextCursor: null, hasMore: false,
    })
    page.resolve({ items: range(1, 30), nextCursor: null, hasMore: false })
    await loading

    expect(store.get('chat-1')?.messages.at(-1)?.text).toBe('edited')
  })
})
