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
