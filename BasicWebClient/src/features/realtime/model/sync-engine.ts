// Keeps the client's state in step with the server's journal (GET /api/sync):
// snapshot -> live events -> catch-up by pts -> ack. Framework-free so it can be tested with fake timers.
//
// Live hub events carry no pts, so they are applied at once (fast path) and a catch-up follows a
// moment later: it replays the journal from the last pts, which the reducers absorb idempotently,
// moves pts forward and acknowledges it (that is what turns the sender's tick into "delivered").

import type { SyncDifferenceDto, SyncStateDto } from '@/shared/api/schema'
import { JOURNALED_EVENT_NAMES, type JournaledEventName, type JournaledEvents } from '@/shared/api/hub.types'

export interface SyncApi {
  getState(): Promise<SyncStateDto>
  getDifference(since: number, limit: number): Promise<SyncDifferenceDto>
  ack(pts: number): Promise<void>
}

export interface SyncSink {
  /** Replace all state with the snapshot. */
  snapshot(state: SyncStateDto): void
  apply<K extends JournaledEventName>(type: K, payload: JournaledEvents[K]): void
}

export interface SyncOptions {
  /** Pause after a live event before catching up: one catch-up per burst. */
  catchUpDelayMs?: number
  ackDelayMs?: number
  pageSize?: number
}

const journaled = new Set<string>(JOURNALED_EVENT_NAMES)

export class SyncEngine {
  private pts: number | null = null
  private ackedPts = 0
  private buffer: { type: JournaledEventName; payload: unknown }[] = []
  private running: Promise<void> | null = null
  private rerun = false
  private catchUpTimer: ReturnType<typeof setTimeout> | undefined
  private ackTimer: ReturnType<typeof setTimeout> | undefined
  private stopped = false

  private readonly catchUpDelayMs: number
  private readonly ackDelayMs: number
  private readonly pageSize: number

  constructor(
    private readonly api: SyncApi,
    private readonly sink: SyncSink,
    options: SyncOptions = {},
  ) {
    this.catchUpDelayMs = options.catchUpDelayMs ?? 1_000
    this.ackDelayMs = options.ackDelayMs ?? 2_000
    this.pageSize = options.pageSize ?? 100
  }

  get currentPts(): number | null {
    return this.pts
  }

  get hasSnapshot(): boolean {
    return this.pts !== null
  }

  /** On every (re)connection: snapshot if there is none yet, then catch up. */
  sync(): Promise<void> {
    this.stopped = false
    clearTimeout(this.catchUpTimer)
    if (this.running) {
      this.rerun = true
      return this.running
    }
    this.running = this.run().finally(() => {
      this.running = null
    })
    return this.running
  }

  /** A live hub event that is also journaled. */
  live<K extends JournaledEventName>(type: K, payload: JournaledEvents[K]): void {
    if (this.stopped) return
    if (this.pts === null) {
      // The snapshot is loading: it may or may not include this; apply after it, the reducers
      // skip what the snapshot already has.
      this.buffer.push({ type, payload })
      return
    }
    this.sink.apply(type, payload)
    this.scheduleCatchUp()
  }

  /** Something changed on the server (e.g. a ChatListUpdated preview): catch up soon. */
  scheduleCatchUp(): void {
    if (this.stopped || this.pts === null) return
    clearTimeout(this.catchUpTimer)
    this.catchUpTimer = setTimeout(() => void this.sync(), this.catchUpDelayMs)
  }

  /** Send the pending ack now (the tab is being hidden or closed). */
  flushAck(): void {
    clearTimeout(this.ackTimer)
    void this.sendAck()
  }

  stop(): void {
    this.stopped = true
    clearTimeout(this.catchUpTimer)
    clearTimeout(this.ackTimer)
    this.pts = null
    this.ackedPts = 0
    this.buffer = []
    this.rerun = false
  }

  private async run(): Promise<void> {
    do {
      this.rerun = false
      try {
        if (this.pts === null) await this.loadSnapshot()
        await this.catchUp()
      } catch (error) {
        // A network failure: the next reconnect, live event or visibility change retries.
        console.warn('Sync failed:', error)
        return
      }
    } while (this.rerun && !this.stopped)
  }

  private async loadSnapshot(): Promise<void> {
    const state = await this.api.getState()
    if (this.stopped) return
    this.sink.snapshot(state)
    this.pts = state.pts

    const buffered = this.buffer
    this.buffer = []
    for (const event of buffered) this.sink.apply(event.type, event.payload as never)
  }

  private async catchUp(): Promise<void> {
    for (;;) {
      if (this.stopped || this.pts === null) return
      const diff = await this.api.getDifference(this.pts, this.pageSize)
      if (this.stopped) return

      if (diff.snapshotRequired) {
        this.pts = null
        await this.loadSnapshot()
        continue
      }

      for (const update of diff.updates) {
        if (journaled.has(update.type)) {
          this.sink.apply(update.type as JournaledEventName, update.payload as never)
        }
      }
      this.pts = Math.max(this.pts, diff.pts)
      if (!diff.hasMore) break
    }
    this.scheduleAck()
  }

  private scheduleAck(): void {
    if (this.pts === null || this.pts <= this.ackedPts) return
    clearTimeout(this.ackTimer)
    this.ackTimer = setTimeout(() => void this.sendAck(), this.ackDelayMs)
  }

  private async sendAck(): Promise<void> {
    const pts = this.pts
    if (pts === null || pts <= this.ackedPts) return
    try {
      await this.api.ack(pts)
      this.ackedPts = Math.max(this.ackedPts, pts)
    } catch {
      // Retried with the next catch-up.
    }
  }
}
