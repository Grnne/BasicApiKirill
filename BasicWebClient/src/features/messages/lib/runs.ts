import type { Message } from '@/entities/message/types'
import { parseApiDate } from '@/shared/lib/date'

const day = (m: Message) => parseApiDate(m.createdAt).toDateString()

/**
 * In a group the sender's photo stands by the last message of their run: the next message is by
 * someone else, is a system one, or is on another day. The others of the run keep its place.
 */
export function closesRun(messages: readonly Message[], index: number): boolean {
  const current = messages[index]
  const next = messages[index + 1]
  if (!current || !next) return true
  return next.senderId !== current.senderId || next.type === 'system' || day(next) !== day(current)
}
