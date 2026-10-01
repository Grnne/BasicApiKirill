import { expect, it } from 'vitest'

import { chat } from '@/testing/fixtures'
import { chatInitial, chatTitle, isMutedNow } from './lib'

it('muted forever, until a moment ahead, or not any more', () => {
  const now = Date.UTC(2026, 9, 1, 12)
  expect(isMutedNow(chat({ isMuted: true, mutedUntil: null }), now)).toBe(true)
  expect(isMutedNow(chat({ isMuted: true, mutedUntil: '2026-10-01T13:00:00Z' }), now)).toBe(true)
  expect(isMutedNow(chat({ isMuted: true, mutedUntil: '2026-10-01T11:00:00Z' }), now)).toBe(false)
  expect(isMutedNow(chat({ isMuted: false }), now)).toBe(false)
})

it('"Saved messages" has its own name and mark', () => {
  const saved = chat({ type: 'saved', title: null, companionId: null, companionName: null })
  expect(chatTitle(saved)).toBe('Избранное')
  expect(chatInitial(saved)).toBe('★')
})
