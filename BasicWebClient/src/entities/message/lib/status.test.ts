import { expect, it } from 'vitest'

import { BOB, ME, chat, message } from '@/testing/fixtures'
import { ownStatus } from './status'

const mine = (seq: number) => message({ seq, senderId: ME })

it('sent, delivered and read come from the chat pointers', () => {
  const c = chat({ outboxDeliveredSeq: 5, outboxReadSeq: 3 })
  expect(ownStatus(mine(3), c, ME)).toBe('read')
  expect(ownStatus(mine(5), c, ME)).toBe('delivered')
  expect(ownStatus(mine(6), c, ME)).toBe('sent')
})

it('no status for others, in Saved messages or for system messages', () => {
  const c = chat({ outboxReadSeq: 10 })
  expect(ownStatus(message({ seq: 1, senderId: BOB }), c, ME)).toBeNull()
  expect(ownStatus(mine(1), chat({ type: 'saved', outboxReadSeq: 10 }), ME)).toBeNull()
  expect(ownStatus(message({ seq: 1, senderId: ME, type: 'system' }), c, ME)).toBeNull()
})
