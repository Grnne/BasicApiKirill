import { describe, expect, it } from 'vitest'

import type { MessageEntityDto } from '@/shared/api/schema'
import { formatText, safeUrl, type FormattedNode } from './formatted'

const e = (type: string, offset: number, length: number, extra: Partial<MessageEntityDto> = {}): MessageEntityDto =>
  ({ type, offset, length, ...extra })

/** A compact view of the tree: text as is, entities as type(children). */
function show(nodes: FormattedNode[]): string {
  return nodes
    .map((n) => (n.kind === 'text' ? n.text : `${n.entity.type}(${show(n.children)})`))
    .join('')
}

describe('formatText', () => {
  it('plain text stays as is', () => {
    expect(show(formatText('hello', []))).toBe('hello')
  })

  it('wraps ranges and keeps the text between them', () => {
    expect(show(formatText('a bold and italic end', [e('bold', 2, 4), e('italic', 11, 6)])))
      .toBe('a bold(bold) and italic(italic) end')
  })

  it('nests an entity inside another', () => {
    expect(show(formatText('bold italic', [e('bold', 0, 11), e('italic', 5, 6)])))
      .toBe('bold(bold italic(italic))')
  })

  it('splits an overlap that is not nesting', () => {
    expect(show(formatText('abcdef', [e('bold', 0, 4), e('italic', 2, 4)])))
      .toBe('bold(abitalic(cd))italic(ef)')
  })

  it('offsets are UTF-16: an emoji outside the BMP counts as two', () => {
    const text = '😀 hi'
    expect(show(formatText(text, [e('bold', 3, 2)]))).toBe('😀 bold(hi)')
  })

  it('clips ranges that go past the text instead of failing', () => {
    expect(show(formatText('abc', [e('bold', 1, 50), e('italic', -5, 6)]))).toBe('italic(a)bold(bc)')
  })

  it('turns bare http(s) URLs into links, without the trailing punctuation', () => {
    expect(show(formatText('see https://example.com/a?b=1, ok', []))).toBe('see link(https://example.com/a?b=1), ok')
  })

  it('does not autolink inside code', () => {
    expect(show(formatText('https://x.y', [e('code', 0, 11)]))).toBe('code(https://x.y)')
  })
})

describe('safeUrl', () => {
  it('lets http, https and mailto through', () => {
    expect(safeUrl('https://example.com/')).toBe('https://example.com/')
    expect(safeUrl('mailto:a@b.c')).toBe('mailto:a@b.c')
  })

  it('refuses script, data and relative URLs', () => {
    expect(safeUrl('javascript:alert(1)')).toBeNull()
    expect(safeUrl(' JaVaScRiPt:alert(1)')).toBeNull()
    expect(safeUrl('data:text/html,<script>')).toBeNull()
    expect(safeUrl('/api/chats')).toBeNull()
    expect(safeUrl(null)).toBeNull()
  })
})
