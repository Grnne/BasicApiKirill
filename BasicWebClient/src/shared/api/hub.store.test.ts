import { flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useHubStore } from './hub.store'

/** A stand-in for one SignalR connection whose start the test finishes by hand. */
class FakeHub {
  state = 'Disconnected'
  invoked: unknown[][] = []
  stopped = false
  handlers = new Map<string, (...args: unknown[]) => void>()
  onReconnected: () => void = () => {}
  private finishStart!: (ok: boolean) => void

  start = vi.fn(
    () =>
      new Promise<void>((resolve, reject) => {
        this.finishStart = (ok) => {
          if (!ok) return reject(new Error('refused'))
          this.state = 'Connected'
          resolve()
        }
      }),
  )
  stop = vi.fn(async () => {
    this.stopped = true
    this.state = 'Disconnected'
  })
  invoke = vi.fn(async (...args: unknown[]) => void this.invoked.push(args))
  on = (event: string, handler: (...args: unknown[]) => void) => void this.handlers.set(event, handler)
  onreconnecting = () => {}
  onreconnected = (handler: () => void) => void (this.onReconnected = handler)
  onclose = () => {}

  connect(ok = true): void {
    this.finishStart(ok)
  }
}

const hubs: FakeHub[] = []

vi.mock('@microsoft/signalr', () => {
  class HubConnectionBuilder {
    withUrl() { return this }
    withAutomaticReconnect() { return this }
    configureLogging() { return this }
    build() {
      const hub = new FakeHub()
      hubs.push(hub)
      return hub
    }
  }
  return { HubConnectionBuilder, HubConnectionState: { Connected: 'Connected' }, LogLevel: { Warning: 3, Error: 4 } }
})

beforeEach(() => {
  setActivePinia(createPinia())
  hubs.length = 0
})

describe('the hub connection', () => {
  it('connects, and an event reaches the subscribers', async () => {
    const store = useHubStore()
    const got: unknown[] = []
    store.on('MessageDeleted', (payload) => void got.push(payload))

    const started = store.start()
    hubs[0]!.connect()
    await started

    expect(store.status).toBe('connected')
    hubs[0]!.handlers.get('MessageDeleted')!({ messageId: 'm-1' })
    expect(got).toEqual([{ messageId: 'm-1' }])
  })

  it('the open chat is joined again after a reconnect: groups do not outlive a connection', async () => {
    const store = useHubStore()
    const started = store.start()
    hubs[0]!.connect()
    await started
    await store.joinChat('c-1')

    hubs[0]!.onReconnected()
    await flushPromises()

    expect(hubs[0]!.invoked.filter((call) => call[0] === 'JoinChat')).toEqual([['JoinChat', 'c-1'], ['JoinChat', 'c-1']])
  })

  it('logout and a new sign-in while connecting: the first connection is dropped, not kept beside the new one', async () => {
    // The bug: both connections ended up assigned, the first one with the old user's token.
    const store = useHubStore()
    void store.start()
    await store.stop()
    void store.start()

    hubs[1]!.connect()
    await flushPromises()
    hubs[0]!.connect()
    await flushPromises()

    expect(hubs[0]!.stopped).toBe(true)
    expect(hubs[1]!.stopped).toBe(false)
    expect(store.status).toBe('connected')
  })
})
