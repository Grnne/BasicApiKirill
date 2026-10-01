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
  failed(chatId: string, clientMessageId: string, error: string): void
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

  async deliver(pending: PendingMessage): Promise<boolean> {
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
          this.sink.failed(pending.chatId, pending.clientMessageId, describeError(error))
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
