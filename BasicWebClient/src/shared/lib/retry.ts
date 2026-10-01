/**
 * Repeats an attempt with growing pauses until it succeeds; the last pause repeats forever.
 * `now()` skips the wait (the network came back, the tab became visible, the user asked).
 */
export class RetryLoop {
  private index = 0
  private timer: ReturnType<typeof setTimeout> | undefined
  private running = false
  private _nextAt: number | null = null

  constructor(
    private readonly attempt: () => Promise<boolean>,
    private readonly delaysMs: readonly number[] = [2_000, 5_000, 15_000, 30_000, 60_000],
  ) {}

  /** When the next attempt is due, or null when none is scheduled. */
  get nextAt(): number | null {
    return this._nextAt
  }

  schedule(): void {
    clearTimeout(this.timer)
    const delay = this.delaysMs[Math.min(this.index, this.delaysMs.length - 1)] ?? 0
    this.index += 1
    this._nextAt = Date.now() + delay
    this.timer = setTimeout(() => void this.run(), delay)
  }

  now(): void {
    clearTimeout(this.timer)
    void this.run()
  }

  /** Success or stop: forget the backoff. */
  cancel(): void {
    clearTimeout(this.timer)
    this.timer = undefined
    this.index = 0
    this._nextAt = null
  }

  private async run(): Promise<void> {
    if (this.running) return
    this.running = true
    this._nextAt = null
    try {
      if (await this.attempt()) this.cancel()
      else this.schedule()
    } finally {
      this.running = false
    }
  }
}
