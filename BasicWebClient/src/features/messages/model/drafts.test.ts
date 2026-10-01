import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import type { DraftDto } from '@/shared/api/schema'
import { DraftSaver, sameDraft, type DraftContent } from './drafts'

const content = (text: string, replyToMessageId: string | null = null): DraftContent =>
  ({ text, entities: [], replyToMessageId })
const dto = (text: string): DraftDto => ({ text, entities: [], replyToMessageId: null, updatedAt: '2026-10-01T12:00:00Z' })

function setup() {
  const api = {
    save: vi.fn(async (_chatId: string, c: DraftContent) => dto(c.text)),
    remove: vi.fn(async (_chatId: string) => {}),
  }
  const saved = vi.fn()
  return { api, saved, saver: new DraftSaver(api, saved, 1_500) }
}

beforeEach(() => vi.useFakeTimers())
afterEach(() => vi.useRealTimers())

describe('DraftSaver', () => {
  it('saved as the tab is hidden, the request is made to outlive the page', async () => {
    // The bug: text typed just before closing the tab was lost with the page.
    const { api, saver } = setup()
    saver.schedule('chat-1', content('last words'))

    saver.flushAll(true)
    await vi.advanceTimersByTimeAsync(0)

    expect(api.save).toHaveBeenCalledWith('chat-1', content('last words'), true)
  })

  it('a save still on its way when the message is sent does not bring the text back', async () => {
    // The bug: the answer to the PUT came after sending and put "hello" back as the draft, and
    // the composer filled itself with the text just sent.
    const { api, saved, saver } = setup()
    let answer!: (d: DraftDto) => void
    api.save.mockImplementationOnce(() => new Promise((resolve) => (answer = resolve)))
    saver.schedule('chat-1', content('hello'))
    await vi.advanceTimersByTimeAsync(1_500)
    expect(saver.isDirty('chat-1')).toBe(true)

    saver.cancel('chat-1')
    answer(dto('hello'))
    await vi.advanceTimersByTimeAsync(0)

    expect(saved).not.toHaveBeenCalledWith('chat-1', dto('hello'))
    // The server may have stored it after the send removed the draft: removed once more.
    expect(api.remove).toHaveBeenCalledWith('chat-1')
  })

  it('of two saves, the answer of the older one does not win', async () => {
    const { api, saved, saver } = setup()
    let first!: (d: DraftDto) => void
    api.save.mockImplementationOnce(() => new Promise((resolve) => (first = resolve)))
    saver.schedule('chat-1', content('a'))
    await vi.advanceTimersByTimeAsync(1_500)
    saver.schedule('chat-1', content('ab'))
    await vi.advanceTimersByTimeAsync(1_500)

    first(dto('a'))
    await vi.advanceTimersByTimeAsync(0)

    expect(saved).toHaveBeenLastCalledWith('chat-1', dto('ab'))
  })

  it('saves once after typing stops', async () => {
    const { api, saved, saver } = setup()

    saver.schedule('chat-1', content('h'))
    await vi.advanceTimersByTimeAsync(500)
    saver.schedule('chat-1', content('hi'))
    expect(saver.isDirty('chat-1')).toBe(true)
    await vi.advanceTimersByTimeAsync(1_500)

    expect(api.save).toHaveBeenCalledTimes(1)
    expect(api.save).toHaveBeenCalledWith('chat-1', content('hi'), false)
    expect(saved).toHaveBeenCalledWith('chat-1', dto('hi'))
    expect(saver.isDirty('chat-1')).toBe(false)
  })

  it('an emptied field removes the draft; a reply alone is still a draft', async () => {
    const { api, saved, saver } = setup()

    saver.schedule('chat-1', content('  '))
    await saver.flush('chat-1')
    expect(api.remove).toHaveBeenCalledWith('chat-1', false)
    expect(saved).toHaveBeenCalledWith('chat-1', null)

    saver.schedule('chat-1', content('', 'msg-1'))
    await saver.flush('chat-1')
    expect(api.save).toHaveBeenCalledTimes(1)
  })

  it('sending cancels the pending save', async () => {
    const { api, saver } = setup()

    saver.schedule('chat-1', content('hi'))
    saver.cancel('chat-1')
    await vi.advanceTimersByTimeAsync(5_000)

    expect(api.save).not.toHaveBeenCalled()
  })

  it('flushing a chat without changes does nothing', async () => {
    const { api, saver } = setup()
    await saver.flush('chat-1')
    expect(api.save).not.toHaveBeenCalled()
    expect(api.remove).not.toHaveBeenCalled()
  })
})

describe('sameDraft', () => {
  it('compares text, reply and formatting', () => {
    expect(sameDraft(content('a'), dto('a'))).toBe(true)
    expect(sameDraft(content('a'), dto('b'))).toBe(false)
    expect(sameDraft(content(''), null)).toBe(true)
    expect(sameDraft(content('', 'm'), null)).toBe(false)
  })
})
