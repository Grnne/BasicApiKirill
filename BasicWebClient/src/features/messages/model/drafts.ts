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
  /** null: the server removed the draft (empty text without a reply). keepalive: the tab is closing. */
  save(chatId: string, content: DraftContent, keepalive?: boolean): Promise<DraftDto | null>
  remove(chatId: string, keepalive?: boolean): Promise<void>
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
  /** Moves on every change of what is typed and on a send: an older save's answer is stale. */
  private readonly versions = new Map<string, number>()
  /** Saves on their way, per chat. */
  private readonly saving = new Map<string, number>()
  private readonly cancelled = new Set<string>()

  constructor(
    private readonly api: DraftApi,
    /** The saved draft, for the chat list; null — none. */
    private readonly saved: (chatId: string, draft: DraftDto | null) => void,
    private readonly delayMs = 1_500,
  ) {}

  /** Has typing not yet saved (or still on its way): an incoming draft must not overwrite it. */
  isDirty(chatId: string): boolean {
    return this.waiting.has(chatId) || (this.saving.get(chatId) ?? 0) > 0
  }

  schedule(chatId: string, content: DraftContent): void {
    this.bump(chatId)
    this.waiting.set(chatId, content)
    clearTimeout(this.timers.get(chatId))
    this.timers.set(chatId, setTimeout(() => void this.flush(chatId), this.delayMs))
  }

  /** Forget what is waiting: the message was sent, and sending removes the draft on the server. */
  cancel(chatId: string): void {
    this.bump(chatId)
    this.drop(chatId)
    if ((this.saving.get(chatId) ?? 0) > 0) this.cancelled.add(chatId)
  }

  async flush(chatId: string, keepalive = false): Promise<void> {
    const content = this.waiting.get(chatId)
    this.drop(chatId)
    if (!content) return
    const version = this.versions.get(chatId)
    this.saving.set(chatId, (this.saving.get(chatId) ?? 0) + 1)
    try {
      const draft = isEmptyDraft(content)
        ? (await this.api.remove(chatId, keepalive), null)
        : await this.api.save(chatId, content, keepalive)
      if (this.versions.get(chatId) === version) this.saved(chatId, draft)
      else if (this.cancelled.has(chatId) && draft) {
        // Sent while this save was on its way: the server may have stored it after the send
        // removed the draft.
        await this.api.remove(chatId)
      }
    } catch {
      // A draft is a convenience: the next keystroke tries again.
    } finally {
      const left = (this.saving.get(chatId) ?? 1) - 1
      if (left > 0) this.saving.set(chatId, left)
      else {
        this.saving.delete(chatId)
        this.cancelled.delete(chatId)
      }
    }
  }

  /** keepalive: the tab is being hidden or closed, and the requests must outlive it. */
  flushAll(keepalive = false): void {
    for (const chatId of [...this.waiting.keys()]) void this.flush(chatId, keepalive)
  }

  private bump(chatId: string): void {
    this.versions.set(chatId, (this.versions.get(chatId) ?? 0) + 1)
  }

  private drop(chatId: string): void {
    clearTimeout(this.timers.get(chatId))
    this.timers.delete(chatId)
    this.waiting.delete(chatId)
  }
}
