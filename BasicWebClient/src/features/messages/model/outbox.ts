// Sends messages over REST. A retry reuses the clientMessageId, so a send whose answer was lost
// does not create a second message: the server answers 200 with the one it already stored.

import type { PendingMessage } from '@/entities/message/model/history'
import { ApiError, NetworkError, describeError } from '@/shared/api/problem'
import type { MessageDto, SendMessageDto } from '@/shared/api/schema'

export interface OutboxApi {
  send(chatId: string, body: SendMessageDto): Promise<MessageDto>
}

export interface OutboxSink {
  stored(message: MessageDto): void
  /** `code` — the server's errorCode, when it answered with one. */
  failed(chatId: string, clientMessageId: string, error: string, code: string | null): void
  sending(chatId: string, clientMessageId: string): void
}

/** Longest Retry-After we wait for on our own; beyond that the user retries. */
const MAX_RATE_LIMIT_WAIT_S = 30

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms))

/** Worth repeating by itself: the request may not have reached the server, or the server hiccupped. */
function isTransient(error: unknown): boolean {
  return error instanceof NetworkError || (error instanceof ApiError && error.status >= 500)
}

export class Outbox {
  constructor(
    private readonly api: OutboxApi,
    private readonly sink: OutboxSink,
    private readonly retryDelaysMs: readonly number[] = [1_000, 3_000, 10_000],
  ) {}

  private readonly queues = new Map<string, Promise<boolean>>()

  /**
   * One chat's messages go one after another: sent together, they would take their order from
   * whichever request the server finished first.
   */
  deliver(pending: PendingMessage): Promise<boolean> {
    const previous = this.queues.get(pending.chatId) ?? Promise.resolve(true)
    const turn = previous.then(() => this.send(pending))
    this.queues.set(pending.chatId, turn)
    void turn.then(() => {
      if (this.queues.get(pending.chatId) === turn) this.queues.delete(pending.chatId)
    })
    return turn
  }

  private async send(pending: PendingMessage): Promise<boolean> {
    const body: SendMessageDto = {
      text: pending.text,
      clientMessageId: pending.clientMessageId,
      entities: pending.entities,
      replyToMessageId: pending.replyToMessageId,
      ...(pending.attachments.length > 0 ? { attachmentIds: pending.attachments.map((a) => a.id) } : {}),
    }
    this.sink.sending(pending.chatId, pending.clientMessageId)

    for (let attempt = 0; ; attempt++) {
      try {
        this.sink.stored(await this.api.send(pending.chatId, body))
        return true
      } catch (error) {
        const delay = this.retryDelay(error, attempt)
        if (delay === null) {
          const code = error instanceof ApiError ? error.code : null
          this.sink.failed(pending.chatId, pending.clientMessageId, describeError(error), code)
          return false
        }
        await sleep(delay)
      }
    }
  }

  private retryDelay(error: unknown, attempt: number): number | null {
    if (error instanceof ApiError && error.isRateLimited) {
      const wait = error.retryAfterSeconds ?? 5
      return attempt === 0 && wait <= MAX_RATE_LIMIT_WAIT_S ? wait * 1000 : null
    }
    if (isTransient(error)) return this.retryDelaysMs[attempt] ?? null
    return null
  }
}
