import type { Message } from '@/entities/message/types'
import { parseApiDate } from '@/shared/lib/date'

const day = (m: Message) => parseApiDate(m.createdAt).toDateString()

/** One sender's messages in a row, on one day, with no system message between them. */
const sameRun = (earlier: Message, later: Message) =>
  later.senderId === earlier.senderId &&
  earlier.type !== 'system' &&
  later.type !== 'system' &&
  day(later) === day(earlier)

/**
 * In a group the sender's photo stands by the last message of their run: the next message is by
 * someone else, is a system one, or is on another day. The others of the run keep its place.
 */
export function closesRun(messages: readonly Message[], index: number): boolean {
  const current = messages[index]
  const next = messages[index + 1]
  return !current || !next || !sameRun(current, next)
}

/** The first message of a run: in a group it carries the sender's name, the rest go close under it. */
export function opensRun(messages: readonly Message[], index: number): boolean {
  const current = messages[index]
  const previous = messages[index - 1]
  return !current || !previous || !sameRun(previous, current)
}
