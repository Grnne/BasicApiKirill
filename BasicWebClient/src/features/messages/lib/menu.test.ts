import { describe, expect, it } from 'vitest'

import { opensBelow, opensToStart } from './menu'

const area = { top: 100, bottom: 700 }

describe('opensBelow', () => {
  it('opens above when the menu fits there', () => {
    expect(opensBelow({ top: 500, bottom: 520 }, area, 250)).toBe(false)
  })

  it('opens below at the top of the list, where the header would cover it', () => {
    expect(opensBelow({ top: 130, bottom: 150 }, area, 250)).toBe(true)
  })

  it('keeps the side with more room when neither fits', () => {
    expect(opensBelow({ top: 300, bottom: 320 }, { top: 100, bottom: 450 }, 250)).toBe(false)
    expect(opensBelow({ top: 200, bottom: 220 }, { top: 100, bottom: 450 }, 250)).toBe(true)
  })
})

describe('opensToStart', () => {
  // A phone: the list is 375 px wide, the menu 230 px.
  const list = { left: 0, right: 375 }

  it('a menu that would run off the right edge opens towards the start instead', () => {
    // The bug: on an incoming message's ⋯ at x≈300 the menu went to x≈530, past the screen.
    expect(opensToStart({ left: 290, right: 310 }, list, 230, 'end')).toBe(true)
  })

  it('keeps the side it prefers while it fits', () => {
    expect(opensToStart({ left: 40, right: 60 }, list, 230, 'end')).toBe(false)
    expect(opensToStart({ left: 300, right: 320 }, list, 230, 'start')).toBe(true)
    expect(opensToStart({ left: 40, right: 60 }, list, 230, 'start')).toBe(false)
  })
})
