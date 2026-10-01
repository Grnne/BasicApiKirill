import { describe, expect, it } from 'vitest'

import { adjustEntities, insertMention, mentionQuery, setLink, toggleEntity, type Entity } from './compose'

const bold = (offset: number, length: number): Entity => ({ type: 'bold', offset, length })
const ranges = (entities: Entity[]) => entities.map((e) => `${e.type}:${e.offset}+${e.length}`)

describe('adjustEntities', () => {
  // "hello world", bold over "world" (6+5)
  const text = 'hello world'
  const entities = [bold(6, 5)]

  it('typing before an entity moves it', () => {
    expect(ranges(adjustEntities(entities, text, 'oh hello world'))).toEqual(['bold:9+5'])
  })

  it('typing inside an entity extends it', () => {
    expect(ranges(adjustEntities(entities, text, 'hello wo-rld'))).toEqual(['bold:6+6'])
  })

  it('typing right after an entity does not', () => {
    expect(ranges(adjustEntities(entities, text, 'hello world!'))).toEqual(['bold:6+5'])
  })

  it('typing right before an entity does not join it', () => {
    expect(ranges(adjustEntities(entities, text, 'hello Xworld'))).toEqual(['bold:7+5'])
  })

  it('deleting part of an entity shrinks it, deleting all of it removes it', () => {
    expect(ranges(adjustEntities(entities, text, 'hello wd'))).toEqual(['bold:6+2'])
    expect(ranges(adjustEntities(entities, text, 'hello '))).toEqual([])
  })

  it('a deletion that starts before the entity cuts its head', () => {
    expect(ranges(adjustEntities(entities, text, 'hellorld'))).toEqual(['bold:5+3'])
  })

  it('replacing the start of a formatted word keeps the formatting', () => {
    expect(ranges(adjustEntities(entities, text, 'hello WOrld'))).toEqual(['bold:6+5'])
  })

  it('works with emoji (two UTF-16 units each)', () => {
    expect(ranges(adjustEntities(entities, text, '😀hello world'))).toEqual(['bold:8+5'])
  })
})

describe('toggleEntity', () => {
  it('adds formatting to a selection', () => {
    expect(ranges(toggleEntity([], 'bold', 0, 5))).toEqual(['bold:0+5'])
  })

  it('removes it when the whole selection already has it, keeping the rest', () => {
    expect(ranges(toggleEntity([bold(0, 11)], 'bold', 3, 6))).toEqual(['bold:0+3', 'bold:6+5'])
  })

  it('merges with an overlapping entity of the same type', () => {
    expect(ranges(toggleEntity([bold(0, 4)], 'bold', 2, 8))).toEqual(['bold:0+8'])
  })

  it('a new link replaces an older one over the same text; no URL removes it', () => {
    const old: Entity[] = [{ type: 'link', offset: 0, length: 4, url: 'https://a.b' }]
    expect(setLink(old, 0, 4, 'https://c.d')).toEqual([{ type: 'link', offset: 0, length: 4, url: 'https://c.d' }])
    expect(setLink(old, 0, 4, null)).toEqual([])
  })
})

describe('mentions', () => {
  it('finds the mention being typed', () => {
    expect(mentionQuery('hi @bo', 6)).toEqual({ start: 3, query: 'bo' })
    expect(mentionQuery('@', 1)).toEqual({ start: 0, query: '' })
    expect(mentionQuery('mail@host', 9)).toBeNull()
    expect(mentionQuery('hi @bo b', 8)).toBeNull()
  })

  it('replaces the query with the name and a mention entity, moving the rest', () => {
    const result = insertMention('hi @bo and bold', [bold(11, 4)], 3, 6, { userId: 'u-1', displayName: 'Борис' })
    expect(result.text).toBe('hi @Борис  and bold')
    expect(result.caret).toBe(10)
    expect(result.entities).toEqual([
      { type: 'mention', offset: 3, length: 6, userId: 'u-1' },
      { type: 'bold', offset: 15, length: 4 },
    ])
  })
})
