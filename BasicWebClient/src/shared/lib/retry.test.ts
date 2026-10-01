import { afterEach, beforeEach, expect, it, vi } from 'vitest'

import { RetryLoop } from './retry'

beforeEach(() => vi.useFakeTimers())
afterEach(() => vi.useRealTimers())

it('keeps trying with growing pauses, the last one forever, until it works', async () => {
  const results = [false, false, false, true]
  const attempt = vi.fn(async () => results.shift() ?? true)
  const loop = new RetryLoop(attempt, [100, 200])

  loop.schedule()
  await vi.advanceTimersByTimeAsync(100)
  expect(attempt).toHaveBeenCalledTimes(1)
  await vi.advanceTimersByTimeAsync(200)
  expect(attempt).toHaveBeenCalledTimes(2)
  await vi.advanceTimersByTimeAsync(200)
  expect(attempt).toHaveBeenCalledTimes(3)
  await vi.advanceTimersByTimeAsync(200)
  expect(attempt).toHaveBeenCalledTimes(4)

  await vi.advanceTimersByTimeAsync(10_000)
  expect(attempt).toHaveBeenCalledTimes(4)
  expect(loop.nextAt).toBeNull()
})

it('now() does not wait for the pause', async () => {
  const attempt = vi.fn(async () => true)
  const loop = new RetryLoop(attempt, [60_000])

  loop.schedule()
  loop.now()
  await vi.advanceTimersByTimeAsync(0)

  expect(attempt).toHaveBeenCalledTimes(1)
  await vi.advanceTimersByTimeAsync(60_000)
  expect(attempt).toHaveBeenCalledTimes(1)
})

it('cancel() stops it and starts the backoff over', async () => {
  const attempt = vi.fn(async () => false)
  const loop = new RetryLoop(attempt, [100, 5_000])

  loop.schedule()
  await vi.advanceTimersByTimeAsync(100)
  loop.cancel()
  await vi.advanceTimersByTimeAsync(10_000)
  expect(attempt).toHaveBeenCalledTimes(1)

  loop.schedule()
  await vi.advanceTimersByTimeAsync(100)
  expect(attempt).toHaveBeenCalledTimes(2)
})
