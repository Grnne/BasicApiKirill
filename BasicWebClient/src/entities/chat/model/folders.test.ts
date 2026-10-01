import { describe, expect, it } from 'vitest'

import type { FolderDto } from '@/shared/api/schema'
import { chat } from '@/testing/fixtures'
import { folderChats } from './folders'

const folder = (overrides: Partial<FolderDto> = {}): FolderDto => ({
  id: 'f-1',
  title: 'Работа',
  includePrivate: false,
  includeGroups: false,
  onlyUnread: false,
  chatIds: [],
  pinnedChatIds: [],
  ...overrides,
})

const at = (minute: number) => new Date(Date.UTC(2026, 9, 1, 12, minute)).toISOString()
const chats = [
  chat({ chatId: 'p1', type: 'private', lastActivityAt: at(1) }),
  chat({ chatId: 'p2', type: 'private', lastActivityAt: at(5), unreadCount: 2 }),
  chat({ chatId: 'g1', type: 'group', lastActivityAt: at(3) }),
  chat({ chatId: 'g2', type: 'group', lastActivityAt: at(4), archived: true }),
  chat({ chatId: 's', type: 'saved', lastActivityAt: at(9) }),
]
const ids = (f: FolderDto) => folderChats(f, chats).map((c) => c.chatId)

describe('folderChats', () => {
  it('by type: archived and Saved messages stay out', () => {
    expect(ids(folder({ includeGroups: true }))).toEqual(['g1'])
    expect(ids(folder({ includePrivate: true }))).toEqual(['p2', 'p1'])
  })

  it('a chat listed explicitly is in even from the archive', () => {
    expect(ids(folder({ chatIds: ['g2', 's'] }))).toEqual(['s', 'g2'])
  })

  it('only unread keeps unread, marked and pinned-in-folder chats', () => {
    expect(ids(folder({ includePrivate: true, includeGroups: true, onlyUnread: true, pinnedChatIds: ['g1'] })))
      .toEqual(['g1', 'p2'])
  })

  it('pinned in the folder go first in their order, the rest by activity', () => {
    expect(ids(folder({ includePrivate: true, includeGroups: true, pinnedChatIds: ['p1', 'g1'] })))
      .toEqual(['p1', 'g1', 'p2'])
  })
})
