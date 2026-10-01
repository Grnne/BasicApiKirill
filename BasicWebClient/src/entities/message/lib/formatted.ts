// Message text + entities -> a tree of nodes to render. Offsets are UTF-16 units, the same as
// JavaScript string indexes, so slicing needs no conversion (an emoji outside the BMP is two).
//
// The server already validates entities; this code still never trusts them: ranges are clipped,
// overlaps that are not nesting are split, and link URLs are re-checked (safeUrl).

import type { MessageEntityDto } from '@/shared/api/schema'

export type FormattedNode =
  | { kind: 'text'; text: string }
  | { kind: 'entity'; entity: MessageEntityDto; children: FormattedNode[] }

interface Range {
  entity: MessageEntityDto
  start: number
  end: number
}

const SAFE_SCHEMES = new Set(['http:', 'https:', 'mailto:'])

/** The URL if it is absolute http/https/mailto, otherwise null (javascript:, data:, relative…). */
export function safeUrl(url: string | null | undefined): string | null {
  if (!url) return null
  try {
    const parsed = new URL(url)
    return SAFE_SCHEMES.has(parsed.protocol) ? parsed.href : null
  } catch {
    return null
  }
}

const byStart = (a: Range, b: Range) => a.start - b.start || b.end - a.end

function build(text: string, start: number, end: number, ranges: Range[]): FormattedNode[] {
  const nodes: FormattedNode[] = []
  let pos = start
  let pending = [...ranges].sort(byStart)

  while (pending.length > 0) {
    const [outer, ...others] = pending as [Range, ...Range[]]
    if (outer.start > pos) nodes.push({ kind: 'text', text: text.slice(pos, outer.start) })

    // Ranges starting inside `outer` are its children; the part of one that sticks out past
    // `outer` continues after it.
    const inner: Range[] = []
    const after: Range[] = []
    for (const range of others) {
      if (range.start >= outer.end) after.push(range)
      else if (range.end <= outer.end) inner.push(range)
      else {
        inner.push({ ...range, end: outer.end })
        after.push({ ...range, start: outer.end })
      }
    }

    nodes.push({ kind: 'entity', entity: outer.entity, children: build(text, outer.start, outer.end, inner) })
    pos = outer.end
    pending = after.sort(byStart)
  }

  if (pos < end) nodes.push({ kind: 'text', text: text.slice(pos, end) })
  return nodes
}

export function formatText(text: string, entities: readonly MessageEntityDto[]): FormattedNode[] {
  const ranges = entities
    .map((entity) => ({
      entity,
      start: Math.max(0, entity.offset),
      end: Math.min(text.length, entity.offset + entity.length),
    }))
    .filter((r) => r.end > r.start)
  return autolink(build(text, 0, text.length, ranges))
}

const URL_PATTERN = /\bhttps?:\/\/[^\s<>"']+/g
const TRAILING_PUNCTUATION = /[.,!?;:)\]}'"»]+$/

/** Bare http(s) URLs in plain text become links, except inside code and existing links. */
function autolink(nodes: FormattedNode[]): FormattedNode[] {
  return nodes.flatMap((node): FormattedNode[] => {
    if (node.kind === 'entity') {
      const keep = ['link', 'code', 'pre', 'mention'].includes(node.entity.type)
      return [keep ? node : { ...node, children: autolink(node.children) }]
    }

    const parts: FormattedNode[] = []
    let pos = 0
    for (const match of node.text.matchAll(URL_PATTERN)) {
      const url = match[0].replace(TRAILING_PUNCTUATION, '')
      const index = match.index
      if (index > pos) parts.push({ kind: 'text', text: node.text.slice(pos, index) })
      parts.push({
        kind: 'entity',
        entity: { type: 'link', offset: 0, length: url.length, url },
        children: [{ kind: 'text', text: url }],
      })
      pos = index + url.length
    }
    if (pos < node.text.length) parts.push({ kind: 'text', text: node.text.slice(pos) })
    return parts.length > 0 ? parts : [node]
  })
}
