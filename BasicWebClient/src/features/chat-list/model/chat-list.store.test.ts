import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useChatsStore } from '@/entities/chat/model/chats.store'
import { ApiError } from '@/shared/api/problem'
import { useNoticesStore } from '@/shared/ui/notices.store'
import { chat, message } from '@/testing/fixtures'
import * as chatsApi from '../api/chats.api'
import { useChatListStore } from './chat-list.store'

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
