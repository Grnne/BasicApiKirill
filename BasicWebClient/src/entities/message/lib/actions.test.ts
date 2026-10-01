import { describe, expect, it } from 'vitest'

import { BOB, ME, message } from '@/testing/fixtures'
import { messageActions, type ActionContext } from './actions'

const sentAt = Date.UTC(2026, 9, 1, 12, 0, 0)
const hours = (h: number) => sentAt + h * 3_600_000
const ctx = (now: number, extra: Partial<ActionContext> = {}): ActionContext =>
  ({ meId: ME, now, editWindowHours: 48, deleteWindowHours: 48, ...extra })
const mine = message({ senderId: ME, createdAt: new Date(sentAt).toISOString() })

describe('messageActions', () => {
  it('own fresh message: edit and delete for everyone', () => {
    const a = messageActions(mine, ctx(hours(1)))
    expect(a.edit).toBe(true)
    expect(a.deleteForEveryone).toBe(true)
  })

  it('after the window: neither, but deleting for me is always possible', () => {
    const a = messageActions(mine, ctx(hours(49)))
    expect(a.edit).toBe(false)
    expect(a.deleteForEveryone).toBe(false)
    expect(a.deleteForMe).toBe(true)
  })

  it('a window of 0 hours means no limit', () => {
    const a = messageActions(mine, ctx(hours(10_000), { editWindowHours: 0, deleteWindowHours: 0 }))
    expect(a.edit).toBe(true)
    expect(a.deleteForEveryone).toBe(true)
  })

  it("someone else's message: no edit, delete for everyone only with the group permission", () => {
    const theirs = message({ senderId: BOB, createdAt: mine.createdAt })
    expect(messageActions(theirs, ctx(hours(1))).edit).toBe(false)
    expect(messageActions(theirs, ctx(hours(1))).deleteForEveryone).toBe(false)
    expect(messageActions(theirs, ctx(hours(1), { canDeleteOthers: true })).deleteForEveryone).toBe(true)
  })

  it('a forwarded message is not editable', () => {
    const forwarded = { ...mine, forwardFrom: { senderId: BOB, senderName: 'Bob' } }
    expect(messageActions(forwarded, ctx(hours(1))).edit).toBe(false)
  })

  it('a system message: no reply, forward, edit or reactions', () => {
    const a = messageActions(message({ type: 'system', senderId: ME }), ctx(hours(0)))
    expect(a).toMatchObject({ reply: false, forward: false, edit: false, react: false, deleteForEveryone: false })
  })
})
