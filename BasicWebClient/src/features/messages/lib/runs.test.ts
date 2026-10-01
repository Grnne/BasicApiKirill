import { expect, it } from 'vitest'

import { BOB, message } from '@/testing/fixtures'
import { closesRun } from './runs'

it('the last message of a sender run carries the photo; a system message or a new day ends the run', () => {
  const list = [
    message({ id: 'a', seq: 1, senderId: BOB, createdAt: '2026-10-01T10:00:00Z' }),
    message({ id: 'b', seq: 2, senderId: BOB, createdAt: '2026-10-01T10:01:00Z' }),
    message({ id: 'c', seq: 3, senderId: 'carl', createdAt: '2026-10-01T10:02:00Z' }),
    message({ id: 'd', seq: 4, senderId: 'carl', type: 'system', createdAt: '2026-10-01T10:03:00Z' }),
    message({ id: 'e', seq: 5, senderId: BOB, createdAt: '2026-10-01T12:00:00Z' }),
    message({ id: 'f', seq: 6, senderId: BOB, createdAt: '2026-10-03T12:00:00Z' }),
  ]

  expect(list.map((_, i) => closesRun(list, i))).toEqual([false, true, true, true, true, true])
})
