import { describe, expect, it } from 'vitest'

import { opensBelow } from './menu'

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
