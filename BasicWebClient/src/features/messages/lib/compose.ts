// Formatting in a plain textarea: entities live beside the text and follow its edits.

import type { MessageEntityDto } from '@/shared/api/schema'

export type Entity = MessageEntityDto

const end = (e: Entity) => e.offset + e.length

/**
 * Moves entities after an edit of the text (typing, deleting, pasting, undo). The edit is the one
 * changed region between the common prefix and suffix. Typing inside an entity extends it; typing
 * right after it does not; deleted parts shrink it; an entity deleted entirely disappears.
 */
export function adjustEntities(entities: readonly Entity[], before: string, after: string): Entity[] {
  if (before === after) return [...entities]

  let start = 0
  const max = Math.min(before.length, after.length)
  while (start < max && before[start] === after[start]) start++
  let suffix = 0
  while (
    suffix < max - start &&
    before[before.length - 1 - suffix] === after[after.length - 1 - suffix]
  ) suffix++

  const oldEnd = before.length - suffix
  const newEnd = after.length - suffix
  const delta = newEnd - oldEnd

  // The edit replaced [start, oldEnd) of the old text with [start, newEnd) of the new one.
  const result: Entity[] = []
  for (const e of entities) {
    const s = e.offset
    const t = end(e)
    let ns: number
    let nt: number
    if (t <= start) {
      // Before the edit, or ending right where it starts: typing after an entity is plain.
      ns = s
      nt = t
    } else if (s >= oldEnd) {
      // After the edit (an insertion right before an entity does not join it either).
      ns = s + delta
      nt = t + delta
    } else {
      // Overlaps the replaced part. Text typed over the inside of an entity takes its formatting
      // when the entity goes on past the edit; the part of it that was deleted is gone.
      ns = s <= start ? s : newEnd
      nt = t > oldEnd ? t + delta : start
    }
    if (nt > ns) result.push({ ...e, offset: ns, length: nt - ns })
  }
  return result
}

/**
 * Bold, italic and the like over a selection: on if any part of it is not covered by that type,
 * off (the covering entities are cut out of the selection) otherwise.
 */
export function toggleEntity(
  entities: readonly Entity[],
  type: string,
  from: number,
  to: number,
  extra: Partial<Entity> = {},
): Entity[] {
  if (to <= from) return [...entities]
  const same = entities.filter((e) => e.type === type)
  const covered = (pos: number) => same.some((e) => e.offset <= pos && pos < end(e))
  let fully = true
  for (let pos = from; pos < to; pos++) if (!covered(pos)) fully = false

  const others = entities.filter((e) => e.type !== type)
  if (fully) {
    const cut: Entity[] = []
    for (const e of same) {
      if (end(e) <= from || e.offset >= to) cut.push(e)
      else {
        if (e.offset < from) cut.push({ ...e, length: from - e.offset })
        if (end(e) > to) cut.push({ ...e, offset: to, length: end(e) - to })
      }
    }
    return sortEntities([...others, ...cut])
  }

  // Merge with touching or overlapping entities of the same type.
  let start = from
  let stop = to
  const kept: Entity[] = []
  for (const e of same) {
    if (e.offset <= stop && end(e) >= start) {
      start = Math.min(start, e.offset)
      stop = Math.max(stop, end(e))
    } else kept.push(e)
  }
  return sortEntities([...others, ...kept, { type, offset: start, length: stop - start, ...extra }])
}

/** A link over the selection: replaces links there; a null URL just removes them. */
export function setLink(entities: readonly Entity[], from: number, to: number, url: string | null): Entity[] {
  if (to <= from) return [...entities]
  const kept: Entity[] = []
  for (const e of entities) {
    if (e.type !== 'link' || end(e) <= from || e.offset >= to) kept.push(e)
    else {
      if (e.offset < from) kept.push({ ...e, length: from - e.offset })
      if (end(e) > to) kept.push({ ...e, offset: to, length: end(e) - to })
    }
  }
  if (url) kept.push({ type: 'link', offset: from, length: to - from, url })
  return sortEntities(kept)
}

export function sortEntities(entities: Entity[]): Entity[] {
  return entities.sort((a, b) => a.offset - b.offset || b.length - a.length)
}

/** "@que|" right before the caret: the mention being typed, if any. */
export function mentionQuery(text: string, caret: number): { start: number; query: string } | null {
  const before = text.slice(0, caret)
  const match = /(^|\s)@([^\s@]{0,32})$/.exec(before)
  if (!match) return null
  return { start: caret - match[2]!.length - 1, query: match[2]! }
}

/** Replaces the typed "@que" with "@Name " and a mention entity for it. */
export function insertMention(
  text: string,
  entities: readonly Entity[],
  start: number,
  caret: number,
  member: { userId: string; displayName: string },
): { text: string; entities: Entity[]; caret: number } {
  const label = `@${member.displayName}`
  const next = `${text.slice(0, start)}${label} ${text.slice(caret)}`
  const moved = adjustEntities(entities, text, `${text.slice(0, start)}${label} ${text.slice(caret)}`)
  // The label itself carries no other formatting.
  const clean = moved.filter((e) => end(e) <= start || e.offset >= start + label.length)
  return {
    text: next,
    entities: sortEntities([...clean, { type: 'mention', offset: start, length: label.length, userId: member.userId }]),
    caret: start + label.length + 1,
  }
}
