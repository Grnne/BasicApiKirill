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
  it('saves once after typing stops', async () => {
    const { api, saved, saver } = setup()

    saver.schedule('chat-1', content('h'))
    await vi.advanceTimersByTimeAsync(500)
    saver.schedule('chat-1', content('hi'))
    expect(saver.isDirty('chat-1')).toBe(true)
    await vi.advanceTimersByTimeAsync(1_500)

    expect(api.save).toHaveBeenCalledTimes(1)
    expect(api.save).toHaveBeenCalledWith('chat-1', content('hi'))
    expect(saved).toHaveBeenCalledWith('chat-1', dto('hi'))
    expect(saver.isDirty('chat-1')).toBe(false)
  })

  it('an emptied field removes the draft; a reply alone is still a draft', async () => {
    const { api, saved, saver } = setup()

    saver.schedule('chat-1', content('  '))
    await saver.flush('chat-1')
    expect(api.remove).toHaveBeenCalledWith('chat-1')
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
