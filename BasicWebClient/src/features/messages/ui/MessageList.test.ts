import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { useChatsStore } from '@/entities/chat/model/chats.store'
import * as messageEntityApi from '@/entities/message/api'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { ME, chat, message } from '@/testing/fixtures'
import * as messagesApi from '../api/messages.api'
import { useMessagesStore } from '../model/messages.store'
import MessageList from './MessageList.vue'

vi.mock('../api/messages.api', () => ({ markRead: vi.fn(async () => {}), sendMessage: vi.fn(), saveDraft: vi.fn(), removeDraft: vi.fn() }))
vi.mock('@/entities/message/api', () => ({
  getMessagesPage: vi.fn(),
  getMessageContext: vi.fn(),
  getMessagesAfter: vi.fn(),
  PAGE_SIZE: 30,
}))
vi.mock('@/entities/chat/api', () => ({ getChatItem: vi.fn(() => new Promise(() => {})), getChatDetail: vi.fn(() => new Promise(() => {})) }))

function setVisibility(state: 'visible' | 'hidden'): void {
  Object.defineProperty(document, 'visibilityState', { value: state, configurable: true })
}

async function openUnreadChat() {
  const pinia = createPinia()
  setActivePinia(pinia)
  useAuthStore().user = { userId: ME, username: 'me', email: 'me@test', displayName: 'Me', avatarId: null }
  const last = message({ seq: 1 })
  useChatsStore().replaceAll([chat({ lastMessage: last, lastReadSeq: 0, unreadCount: 1 })], [])
  vi.mocked(messageEntityApi.getMessagesPage).mockResolvedValue({ items: [{ ...last }], nextCursor: null, hasMore: false })
  mount(MessageList, { global: { plugins: [pinia] } })
  await useMessagesStore().openChat('chat-1')
  await flushPromises()
  await vi.advanceTimersByTimeAsync(1_000)
}

beforeEach(() => {
  vi.useFakeTimers()
  vi.mocked(messagesApi.markRead).mockClear()
})
afterEach(() => {
  vi.useRealTimers()
  setVisibility('visible')
})

describe('reading what is on screen', () => {
  it('an opened chat is read when the tab is visible', async () => {
    setVisibility('visible')
    await openUnreadChat()

    expect(messagesApi.markRead).toHaveBeenCalledWith('chat-1', expect.any(String))
  })

  it('a chat loaded in a hidden tab stays unread', async () => {
    setVisibility('hidden')
    await openUnreadChat()

    expect(messagesApi.markRead).not.toHaveBeenCalled()
  })
})
