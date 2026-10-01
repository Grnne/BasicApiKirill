import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import type { PendingMessage } from '@/entities/message/model/history'
import { ApiError, NetworkError } from '@/shared/api/problem'
import type { MessageDto, SendMessageDto } from '@/shared/api/schema'
import { ME, message } from '@/testing/fixtures'
import { Outbox, type OutboxSink } from './outbox'

const pending: PendingMessage = {
  clientMessageId: 'cm-1',
  chatId: 'chat-1',
  text: 'hi',
  entities: [],
  replyToMessageId: null,
  createdAt: new Date().toISOString(),
  state: 'sending',
  error: null,
}

function setup(results: (MessageDto | Error)[]) {
  const send = vi.fn(async (_chatId: string, _body: SendMessageDto) => {
    const next = results.shift()
    if (!next || next instanceof Error) throw next ?? new Error('no result')
    return next
  })
  const sink = { stored: vi.fn(), failed: vi.fn(), sending: vi.fn() } satisfies OutboxSink
  return { send, sink, outbox: new Outbox({ send }, sink, [100, 200]) }
}

beforeEach(() => vi.useFakeTimers())
afterEach(() => vi.useRealTimers())

describe('Outbox', () => {
  it('sends the text with its clientMessageId and hands over the stored message', async () => {
    const stored = message({ senderId: ME, clientMessageId: 'cm-1' })
    const { send, sink, outbox } = setup([stored])

    expect(await outbox.deliver(pending)).toBe(true)

    expect(send).toHaveBeenCalledWith('chat-1', expect.objectContaining({ text: 'hi', clientMessageId: 'cm-1' }))
    expect(sink.stored).toHaveBeenCalledWith(stored)
  })

  it('repeats after a network failure with the same clientMessageId', async () => {
    const { send, sink, outbox } = setup([new NetworkError(new TypeError()), message({ clientMessageId: 'cm-1' })])

    const done = outbox.deliver(pending)
    await vi.advanceTimersByTimeAsync(100)

    expect(await done).toBe(true)
    expect(send).toHaveBeenCalledTimes(2)
    expect(send.mock.calls.map((c) => c[1].clientMessageId)).toEqual(['cm-1', 'cm-1'])
    expect(sink.failed).not.toHaveBeenCalled()
  })

  it('gives up after the last retry and says why', async () => {
    const offline = () => new NetworkError(new TypeError())
    const { sink, outbox } = setup([offline(), offline(), offline()])

    const done = outbox.deliver(pending)
    await vi.advanceTimersByTimeAsync(300)

    expect(await done).toBe(false)
    expect(sink.failed).toHaveBeenCalledWith('chat-1', 'cm-1', 'Нет связи с сервером')
  })

  it('does not repeat a refusal: the reason goes to the user', async () => {
    // Before: any failure said "check the connection", even a block by the recipient.
    const refused = new ApiError(403, { errorCode: 'PRIVACY_RESTRICTED' })
    const { send, sink, outbox } = setup([refused])

    expect(await outbox.deliver(pending)).toBe(false)

    expect(send).toHaveBeenCalledTimes(1)
    expect(sink.failed).toHaveBeenCalledWith('chat-1', 'cm-1', 'Пользователь ограничил, кто может ему писать или добавлять его')
  })

  it('waits out a short rate limit once', async () => {
    const { send, outbox } = setup([new ApiError(429, { errorCode: 'RATE_LIMITED' }, 2), message()])

    const done = outbox.deliver(pending)
    await vi.advanceTimersByTimeAsync(1_999)
    expect(send).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(1)

    expect(await done).toBe(true)
  })
})
