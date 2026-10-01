// Saves what is typed as the chat's draft, shared by all the user's devices (PUT /draft).
// A pause after typing stops is one request (the endpoint counts against the command limit);
// leaving the chat saves at once.

import type { DraftDto, MessageEntityDto } from '@/shared/api/schema'

export interface DraftContent {
  text: string
  entities: MessageEntityDto[]
  replyToMessageId: string | null
}

export interface DraftApi {
  /** null: the server removed the draft (empty text without a reply). */
  save(chatId: string, content: DraftContent): Promise<DraftDto | null>
  remove(chatId: string): Promise<void>
}

export const isEmptyDraft = (c: DraftContent) => c.text.trim() === '' && c.replyToMessageId === null

export function sameDraft(a: DraftContent, b: DraftDto | null): boolean {
  if (!b) return isEmptyDraft(a)
  return (
    a.text === b.text &&
    a.replyToMessageId === b.replyToMessageId &&
    JSON.stringify(a.entities) === JSON.stringify(b.entities)
  )
}

export class DraftSaver {
  private readonly timers = new Map<string, ReturnType<typeof setTimeout>>()
  private readonly waiting = new Map<string, DraftContent>()

  constructor(
    private readonly api: DraftApi,
    /** The saved draft, for the chat list; null — none. */
    private readonly saved: (chatId: string, draft: DraftDto | null) => void,
    private readonly delayMs = 1_500,
  ) {}

  /** Has typing not yet saved: an incoming draft must not overwrite it. */
  isDirty(chatId: string): boolean {
    return this.waiting.has(chatId)
  }

  schedule(chatId: string, content: DraftContent): void {
    this.waiting.set(chatId, content)
    clearTimeout(this.timers.get(chatId))
    this.timers.set(chatId, setTimeout(() => void this.flush(chatId), this.delayMs))
  }

  /** Forget what is waiting: the message was sent, and sending removes the draft on the server. */
  cancel(chatId: string): void {
    clearTimeout(this.timers.get(chatId))
    this.timers.delete(chatId)
    this.waiting.delete(chatId)
  }

  async flush(chatId: string): Promise<void> {
    const content = this.waiting.get(chatId)
    this.cancel(chatId)
    if (!content) return
    try {
      if (isEmptyDraft(content)) {
        await this.api.remove(chatId)
        this.saved(chatId, null)
      } else {
        this.saved(chatId, await this.api.save(chatId, content))
      }
    } catch {
      // A draft is a convenience: the next keystroke tries again.
    }
  }

  flushAll(): void {
    for (const chatId of [...this.waiting.keys()]) void this.flush(chatId)
  }
}
