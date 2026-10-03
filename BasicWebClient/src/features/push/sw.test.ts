// The service worker is plain JavaScript in public/; it is run here against a fake `self`.

import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const code = readFileSync(resolve(__dirname, '../../../public/sw.js'), 'utf-8')
const SCOPE = 'https://chat.example/client/'

type Handler = (event: unknown) => void

function loadWorker(windows: { url: string; focus: () => Promise<void>; postMessage: (m: unknown) => void }[] = []) {
  const handlers: Record<string, Handler> = {}
  const self = {
    addEventListener: (type: string, handler: Handler) => (handlers[type] = handler),
    skipWaiting: vi.fn(),
    registration: { scope: SCOPE, showNotification: vi.fn(async () => {}) },
    clients: { claim: vi.fn(), matchAll: vi.fn(async () => windows), openWindow: vi.fn(async () => null) },
  }
  new Function('self', code)(self)

  async function fire(type: string, event: Record<string, unknown>): Promise<void> {
    let pending: Promise<unknown> = Promise.resolve()
    handlers[type]!({ ...event, waitUntil: (p: Promise<unknown>) => (pending = p) })
    await pending
  }
  return { self, fire }
}

const push = (payload: unknown) => ({ data: { json: () => payload } })
const base = { kind: 'message', chatId: 'c1', senderName: 'Алиса', messageType: 'text', text: 'привет' }

describe('service worker', () => {
  let worker: ReturnType<typeof loadWorker>
  beforeEach(() => {
    worker = loadWorker()
  })

  it('a private message: titled by the sender; one notification per chat', async () => {
    await worker.fire('push', push({ ...base, chatType: 'private' }))

    expect(worker.self.registration.showNotification).toHaveBeenCalledWith('Алиса', {
      body: 'привет', tag: 'chat-c1', renotify: true, data: { chatId: 'c1' },
    })
  })

  it('a group: titled by the group, the sender before the text; files without a caption say what they are', async () => {
    await worker.fire('push', push({ ...base, chatType: 'group', chatTitle: 'Команда', text: '', attachmentKind: 'photo', attachmentCount: 3 }))

    expect(worker.self.registration.showNotification).toHaveBeenCalledWith(
      'Команда',
      expect.objectContaining({ body: 'Алиса: Фото (3)' }),
    )
  })

  it('a reaction to my message: who and what, apart from the messages of the chat', async () => {
    const reaction = { ...base, kind: 'reaction', emoji: '🔥', messageId: 'm1' }
    await worker.fire('push', push({ ...reaction, chatType: 'private' }))
    await worker.fire('push', push({ ...reaction, chatType: 'group', chatTitle: 'Команда', text: '', attachmentKind: 'photo', attachmentCount: 1 }))

    expect(worker.self.registration.showNotification).toHaveBeenNthCalledWith(1, 'Алиса', {
      body: '🔥 на «привет»', tag: 'reaction-m1', renotify: true, data: { chatId: 'c1' },
    })
    expect(worker.self.registration.showNotification).toHaveBeenNthCalledWith(2, 'Команда',
      expect.objectContaining({ body: 'Алиса: 🔥 на «Фото»' }))
  })

  it('the open client asks for the same notification as push would show', async () => {
    await worker.fire('message', { data: { type: 'show', payload: { ...base, chatType: 'private' } } })
    await worker.fire('message', { data: { type: 'something-else' } })

    expect(worker.self.registration.showNotification).toHaveBeenCalledOnce()
    expect(worker.self.registration.showNotification).toHaveBeenCalledWith('Алиса', expect.objectContaining({ tag: 'chat-c1' }))
  })

  it('an unknown kind or a broken payload shows nothing', async () => {
    await worker.fire('push', push({ kind: 'something_new', chatId: 'c1' }))
    await worker.fire('push', { data: { json: () => { throw new Error('not json') } } })

    expect(worker.self.registration.showNotification).not.toHaveBeenCalled()
  })

  it('a click opens the chat: in an open tab of the client, or in a new one', async () => {
    const close = vi.fn()
    await worker.fire('notificationclick', { notification: { close, data: { chatId: 'c 1' } } })
    expect(close).toHaveBeenCalled()
    expect(worker.self.clients.openWindow).toHaveBeenCalledWith(`${SCOPE}chat?open=c%201`)

    const tab = { url: `${SCOPE}chat`, focus: vi.fn(async () => {}), postMessage: vi.fn() }
    const withTab = loadWorker([{ url: 'https://other.example/', focus: vi.fn(), postMessage: vi.fn() }, tab])
    await withTab.fire('notificationclick', { notification: { close, data: { chatId: 'c1' } } })

    expect(tab.focus).toHaveBeenCalled()
    expect(tab.postMessage).toHaveBeenCalledWith({ type: 'open-chat', chatId: 'c1' })
    expect(withTab.self.clients.openWindow).not.toHaveBeenCalled()
  })
})
