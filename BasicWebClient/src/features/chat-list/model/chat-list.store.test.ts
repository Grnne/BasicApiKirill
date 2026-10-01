import { flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useChatsStore } from '@/entities/chat/model/chats.store'
import { ApiError } from '@/shared/api/problem'
import { useNoticesStore } from '@/shared/ui/notices.store'
import { chat, message } from '@/testing/fixtures'
import * as chatsApi from '../api/chats.api'
import * as foldersApi from '../api/folders.api'
import { useChatListStore } from './chat-list.store'

vi.mock('../api/folders.api', () => ({
  createFolder: vi.fn(),
  updateFolder: vi.fn(),
  deleteFolder: vi.fn(async () => {}),
  reorderFolders: vi.fn(),
}))
vi.mock('../api/chats.api', () => ({
  setMarkedUnread: vi.fn(async () => {}),
  markRead: vi.fn(async () => {}),
  setPinned: vi.fn(),
  reorderPinned: vi.fn(async () => {}),
  setArchived: vi.fn(),
  setMuted: vi.fn(),
  createPrivateChat: vi.fn(),
  searchChats: vi.fn(),
}))

beforeEach(() => {
  setActivePinia(createPinia())
  vi.mocked(chatsApi.markRead).mockClear()
  vi.mocked(chatsApi.setMarkedUnread).mockClear()
})

describe('marking chats', () => {
  it('marks a chat unread at once and tells the server', async () => {
    const chats = useChatsStore()
    chats.replaceAll([chat()], [])

    await useChatListStore().markUnread('chat-1')

    expect(chats.get('chat-1')!.markedUnread).toBe(true)
    expect(chatsApi.setMarkedUnread).toHaveBeenCalledWith('chat-1', true)
  })

  it('reading a chat from the menu reads up to its last message', async () => {
    const chats = useChatsStore()
    const last = message({ seq: 4 })
    chats.replaceAll([chat({ lastMessage: last, unreadCount: 2, markedUnread: true })], [])

    await useChatListStore().markRead('chat-1')

    expect(chatsApi.markRead).toHaveBeenCalledWith('chat-1', last.id)
    expect(chats.get('chat-1')).toMatchObject({ unreadCount: 0, markedUnread: false, lastReadSeq: 4 })
  })

  it('a failed command tells the user', async () => {
    useChatsStore().replaceAll([chat()], [])
    vi.mocked(chatsApi.setMarkedUnread).mockRejectedValueOnce(new ApiError(403, { errorCode: 'NOT_A_MEMBER' }))

    await useChatListStore().markUnread('chat-1')

    expect(useNoticesStore().items[0]!.text).toBe('Вы не участник этого чата')
  })
})

describe('opening a private chat', () => {
  it('a refusal is shown, not swallowed', async () => {
    // Before: a person who allows no new chats was a click that did nothing.
    useChatsStore().replaceAll([], [])
    vi.mocked(chatsApi.createPrivateChat).mockRejectedValueOnce(new ApiError(403, { errorCode: 'PRIVACY_RESTRICTED' }))

    await useChatListStore().openPrivateChat('bob')

    expect(useNoticesStore().items[0]!.text).toBe('Пользователь ограничил, кто может ему писать или добавлять его')
    expect(useChatListStore().selectedChatId).toBeNull()
  })
})

describe('pinned chats', () => {
  it('pinning puts the order from the answer and takes the chat out of the archive', async () => {
    const chats = useChatsStore()
    chats.replaceAll([chat({ chatId: 'a', archived: true }), chat({ chatId: 'b', pinnedPosition: 1 })], [])
    vi.mocked(chatsApi.setPinned).mockResolvedValue({ chatIds: ['a', 'b'] })

    await useChatListStore().pin('a', true)

    expect(chats.list.map((c) => c.chatId)).toEqual(['a', 'b'])
    expect(chats.get('a')).toMatchObject({ pinnedPosition: 1, archived: false })
  })

  it('a new order is shown at once and sent', async () => {
    const chats = useChatsStore()
    chats.replaceAll([chat({ chatId: 'a', pinnedPosition: 1 }), chat({ chatId: 'b', pinnedPosition: 2 })], [])

    const done = useChatListStore().reorderPinned(['b', 'a'])
    expect(chats.list.map((c) => c.chatId)).toEqual(['b', 'a'])
    await done

    expect(chatsApi.reorderPinned).toHaveBeenCalledWith(['b', 'a'])
  })
})

describe('archive and mute', () => {
  it('archiving moves the chat to the archive list and unpins it', async () => {
    const chats = useChatsStore()
    chats.replaceAll([chat({ chatId: 'a', pinnedPosition: 1 }), chat({ chatId: 'b' })], [])
    vi.mocked(chatsApi.setArchived).mockResolvedValue({ chatId: 'a', archived: true, isMuted: false, mutedUntil: null })

    await useChatListStore().archive('a', true)

    expect(chats.mainList.map((c) => c.chatId)).toEqual(['b'])
    expect(chats.archivedList.map((c) => c.chatId)).toEqual(['a'])
    expect(chats.get('a')!.pinnedPosition).toBeNull()
  })

  it('mute sends the end moment; forever sends none', async () => {
    const chats = useChatsStore()
    chats.replaceAll([chat()], [])
    vi.mocked(chatsApi.setMuted).mockResolvedValue({ chatId: 'chat-1', archived: false, isMuted: true, mutedUntil: null })

    await useChatListStore().mute('chat-1', Date.UTC(2026, 9, 1, 13))
    expect(chatsApi.setMuted).toHaveBeenLastCalledWith('chat-1', true, '2026-10-01T13:00:00.000Z')

    await useChatListStore().mute('chat-1', null)
    expect(chatsApi.setMuted).toHaveBeenLastCalledWith('chat-1', true, undefined)
    expect(chats.get('chat-1')!.isMuted).toBe(true)
  })
})

describe('folders', () => {
  const f = (id: string, title = id) => ({
    id, title, includePrivate: true, includeGroups: false, onlyUnread: false, chatIds: [], pinnedChatIds: [],
  })

  it('a new folder goes last; an edited one keeps its place', async () => {
    const chats = useChatsStore()
    chats.replaceAll([], [f('a'), f('b')])
    vi.mocked(foldersApi.createFolder).mockResolvedValue(f('c'))
    vi.mocked(foldersApi.updateFolder).mockResolvedValue(f('a', 'Работа'))

    await useChatListStore().saveFolder(null, { title: 'c' })
    await useChatListStore().saveFolder('a', { title: 'Работа' })

    expect(chats.folders.map((x) => x.title)).toEqual(['Работа', 'b', 'c'])
  })

  it('deleting the open folder goes back to all chats', async () => {
    const chats = useChatsStore()
    chats.replaceAll([], [f('a')])
    const store = useChatListStore()
    store.selectedFolderId = 'a'

    await store.deleteFolder('a')

    expect(chats.folders).toEqual([])
    expect(store.selectedFolderId).toBeNull()
  })

  it('pinning in a folder puts the chat on top of the folder pins', async () => {
    const chats = useChatsStore()
    chats.replaceAll([], [{ ...f('a'), pinnedChatIds: ['x'] }])
    vi.mocked(foldersApi.updateFolder).mockImplementation(async (_id, body) => ({ ...f('a'), pinnedChatIds: body.pinnedChatIds ?? [] }))

    await useChatListStore().pinInFolder(chats.folders[0]!, 'y', true)

    expect(foldersApi.updateFolder).toHaveBeenCalledWith('a', { pinnedChatIds: ['y', 'x'] })
    expect(chats.folders[0]!.pinnedChatIds).toEqual(['y', 'x'])
  })

  it('unpinning a chat the folder holds by its type does not leave it listed by name', async () => {
    // The bug: the server lists every pinned chat by name; unpinned, it stayed listed and kept
    // showing in the folder after it was archived.
    const chats = useChatsStore()
    chats.replaceAll([chat({ chatId: 'p1', type: 'private' })], [{ ...f('a'), chatIds: ['p1', 'g9'], pinnedChatIds: ['p1'] }])
    vi.mocked(foldersApi.updateFolder).mockImplementation(async (_id, body) => ({ ...f('a'), ...body }) as never)

    await useChatListStore().pinInFolder(chats.folders[0]!, 'p1', false)

    expect(foldersApi.updateFolder).toHaveBeenLastCalledWith('a', { pinnedChatIds: [], chatIds: ['g9'] })
  })
})

describe('the open chat goes away', () => {
  it('removed from the open group: a notice, and the chat closes', async () => {
    const chats = useChatsStore()
    chats.replaceAll([chat({ chatId: 'g1', type: 'group', title: 'Team' })], [])
    const list = useChatListStore()
    await list.select('g1')

    chats.apply('MemberRemoved', { chatId: 'g1', userId: 'me', removedBy: 'bob' }, { meId: 'me' })
    await flushPromises()

    expect(list.selectedChatId).toBeNull()
    expect(useNoticesStore().items.map((n) => n.text)).toContain('Вы больше не участник группы «Team»')
  })

  it('left or deleted by the user: the event may come before the answer, and no notice is shown', async () => {
    const chats = useChatsStore()
    chats.replaceAll([chat({ chatId: 'g1', type: 'group', title: 'Team' })], [])
    const list = useChatListStore()
    await list.select('g1')

    list.expectGone('g1')
    chats.apply('ChatDeleted', { chatId: 'g1' }, { meId: 'me' })
    await flushPromises()

    expect(list.selectedChatId).toBeNull()
    expect(useNoticesStore().items).toEqual([])
  })

  it('another chat going away leaves the open one alone', async () => {
    const chats = useChatsStore()
    chats.replaceAll([chat({ chatId: 'g1', type: 'group' }), chat({ chatId: 'g2', type: 'group' })], [])
    const list = useChatListStore()
    await list.select('g1')

    chats.apply('ChatDeleted', { chatId: 'g2' }, { meId: 'me' })
    await flushPromises()

    expect(list.selectedChatId).toBe('g1')
  })
})
