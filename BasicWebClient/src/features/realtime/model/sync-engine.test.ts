import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { message } from '@/testing/fixtures'
import type { SyncDifferenceDto, SyncStateDto, SyncUpdateDto } from '@/shared/api/schema'
import { SyncEngine, type SyncApi, type SyncSink } from './sync-engine'

function snapshot(pts: number): SyncStateDto {
  return {
    pts,
    chats: [],
    folders: [],
    privacy: { lastSeen: 'everybody', messages: 'everybody', groupAdd: 'everybody' },
    blockedUserIds: [],
    me: { userId: 'me', username: 'me', email: 'me@test', displayName: 'Me', avatarId: null },
  }
}

const update = (pts: number, type = 'MessageCreated', payload: unknown = message({ seq: pts })): SyncUpdateDto =>
  ({ pts, type, payload, createdAt: new Date().toISOString() })

const diff = (updates: SyncUpdateDto[], pts: number, extra: Partial<SyncDifferenceDto> = {}): SyncDifferenceDto =>
  ({ updates, pts, hasMore: false, snapshotRequired: false, ...extra })

function setup(differences: SyncDifferenceDto[], state = snapshot(10)) {
  const api = {
    getState: vi.fn(async () => state),
    getDifference: vi.fn(async (_since: number, _limit: number) => differences.shift() ?? diff([], state.pts)),
    ack: vi.fn(async (_pts: number) => {}),
  } satisfies SyncApi
  const log: string[] = []
  const sink: SyncSink = {
    snapshot: (s) => log.push(`snapshot ${s.pts}`),
    apply: (type, payload) => log.push(`${type} ${(payload as { seq?: number }).seq ?? ''}`.trim()),
  }
  const engine = new SyncEngine(api, sink, { catchUpDelayMs: 1000, ackDelayMs: 2000 })
  return { api, log, engine }
}

beforeEach(() => vi.useFakeTimers())
afterEach(() => vi.useRealTimers())

describe('SyncEngine', () => {
  it('loads the snapshot, then catches up from its pts across pages', async () => {
    const { api, log, engine } = setup([
      diff([update(11), update(12)], 12, { hasMore: true }),
      diff([update(13)], 13),
    ])

    await engine.sync()

    expect(api.getDifference.mock.calls.map((c) => c[0])).toEqual([10, 12])
    expect(log).toEqual(['snapshot 10', 'MessageCreated 11', 'MessageCreated 12', 'MessageCreated 13'])
    expect(engine.currentPts).toBe(13)
  })

  it('applies live events buffered during the snapshot after it', async () => {
    const { log, engine } = setup([])
    const done = engine.sync()
    engine.live('MessageCreated', message({ seq: 7 }))
    await done

    expect(log).toEqual(['snapshot 10', 'MessageCreated 7'])
  })

  it('a live event is applied at once and followed by one catch-up per burst', async () => {
    const { api, log, engine } = setup([])
    await engine.sync()
    api.getDifference.mockClear()

    engine.live('MessageCreated', message({ seq: 11 }))
    engine.live('MessageCreated', message({ seq: 12 }))
    expect(log.slice(-2)).toEqual(['MessageCreated 11', 'MessageCreated 12'])
    expect(api.getDifference).not.toHaveBeenCalled()

    await vi.advanceTimersByTimeAsync(1000)
    expect(api.getDifference).toHaveBeenCalledTimes(1)
  })

  it('starts over from a snapshot when the journal no longer goes back that far', async () => {
    const { api, log, engine } = setup([diff([], 10, { snapshotRequired: true }), diff([update(51)], 51)])
    api.getState.mockResolvedValueOnce(snapshot(10)).mockResolvedValueOnce(snapshot(50))

    await engine.sync()

    expect(log).toEqual(['snapshot 10', 'snapshot 50', 'MessageCreated 51'])
    expect(engine.currentPts).toBe(51)
  })

  it('acknowledges the caught-up pts after a pause, once', async () => {
    const { api, engine } = setup([diff([update(11)], 11)])

    await engine.sync()
    expect(api.ack).not.toHaveBeenCalled()

    await vi.advanceTimersByTimeAsync(2000)
    expect(api.ack).toHaveBeenCalledWith(11)

    await engine.sync()
    await vi.advanceTimersByTimeAsync(2000)
    expect(api.ack).toHaveBeenCalledTimes(1)
  })

  it('skips journal entries of types it does not know', async () => {
    const { log, engine } = setup([diff([update(11, 'SomethingNew', {}), update(12)], 12)])

    await engine.sync()

    expect(log).toEqual(['snapshot 10', 'MessageCreated 12'])
  })

  it('a sync requested while one runs runs again after it', async () => {
    const { api, engine } = setup([])
    const first = engine.sync()
    const second = engine.sync()
    await first
    await second

    expect(api.getState).toHaveBeenCalledTimes(1)
    expect(api.getDifference).toHaveBeenCalledTimes(2)
  })

  it('a failed catch-up keeps pts and retries on the next sync', async () => {
    const { api, engine } = setup([])
    await engine.sync()
    api.getDifference.mockRejectedValueOnce(new Error('offline'))
    vi.spyOn(console, 'warn').mockImplementation(() => {})

    await engine.sync()
    expect(engine.currentPts).toBe(10)

    await engine.sync()
    expect(api.getDifference).toHaveBeenCalledTimes(3)
  })

  it('after stop nothing is applied and the next sync starts from a snapshot', async () => {
    const { api, log, engine } = setup([])
    await engine.sync()
    engine.stop()

    engine.live('MessageCreated', message({ seq: 11 }))
    expect(log).toEqual(['snapshot 10'])

    await engine.sync()
    expect(api.getState).toHaveBeenCalledTimes(2)
  })

  it('a failed first snapshot is tried again by itself', async () => {
    // The bug: a 502 on the snapshot left "loading…" for good while the hub said "connected".
    const { api, log, engine } = setup([])
    api.getState.mockRejectedValueOnce(new Error('502'))

    await engine.sync()
    expect(engine.hasSnapshot).toBe(false)
    await vi.advanceTimersByTimeAsync(2_000)

    expect(log[0]).toBe('snapshot 10')
    expect(engine.hasSnapshot).toBe(true)
  })

  it('a steady stream of events still gets a catch-up within three seconds', async () => {
    // The bug: every event moved the catch-up a second further, so a busy chat held it off.
    const { api, engine } = setup([])
    await engine.sync()
    api.getDifference.mockClear()

    for (let i = 0; i < 10; i++) {
      engine.scheduleCatchUp()
      await vi.advanceTimersByTimeAsync(500)
    }

    expect(api.getDifference.mock.calls.length).toBeGreaterThanOrEqual(1)
  })

  it('a run of a signed-out session is not applied to the next sign-in', async () => {
    let answer!: (s: SyncStateDto) => void
    const { api, log, engine } = setup([])
    api.getState.mockImplementationOnce(() => new Promise((resolve) => (answer = resolve)))
    void engine.sync()

    engine.stop()
    const next = engine.sync()
    answer(snapshot(99))
    await next

    expect(log).not.toContain('snapshot 99')
  })
})

