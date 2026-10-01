import { describe, expect, it } from 'vitest'

import { parseApiDate } from './date'

describe('parseApiDate', () => {
  it('reads a zone-less string as UTC', () => {
    expect(parseApiDate('2026-09-29T18:04:05').toISOString()).toBe('2026-09-29T18:04:05.000Z')
  })

  it('keeps an explicit zone', () => {
    expect(parseApiDate('2026-09-29T18:04:05.123456Z').toISOString()).toBe('2026-09-29T18:04:05.123Z')
    expect(parseApiDate('2026-09-29T21:04:05+03:00').toISOString()).toBe('2026-09-29T18:04:05.000Z')
  })
})
