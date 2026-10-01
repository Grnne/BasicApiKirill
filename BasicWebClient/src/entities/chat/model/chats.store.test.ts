import { flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, expect, it, vi } from 'vitest'

import { BOB, ME, chat, message } from '@/testing/fixtures'
import * as chatApi from '../api'
import { useChatsStore } from './chats.store'

vi.mock('../api', () => ({ getChatItem: vi.fn() }))

beforeEach(() => setActivePinia(createPinia()))

it('the last message deleted: the reloaded row shows the one before it, though it is older', async () => {
  // The bug: the reload was taken for a stale answer (its last message has a lower seq) and the
  // deleted message stayed in the list preview.
  const first = message({ id: 'm1', seq: 1, senderId: BOB, text: 'first' })
  const second = message({ id: 'm2', seq: 2, senderId: BOB, text: 'second' })
  const chats = useChatsStore()
  chats.replaceAll([chat({ lastMessage: second, lastReadSeq: 2 })], [])
  vi.mocked(chatApi.getChatItem).mockResolvedValue(chat({ lastMessage: first, lastReadSeq: 2 }))

  chats.apply('MessageDeleted', { chatId: 'chat-1', messageId: 'm2', seq: 2, forEveryone: true }, { meId: ME })
  await flushPromises()

  expect(chats.get('chat-1')!.lastMessage!.text).toBe('first')
})

it('a message that arrived before the reloaded row still wins over it', async () => {
  const chats = useChatsStore()
  chats.replaceAll([chat({ lastMessage: message({ id: 'm1', seq: 1 }) })], [])
  chats.apply('MessageCreated', message({ id: 'm3', seq: 3, text: 'newest' }), { meId: ME })

  chats.put(chat({ lastMessage: message({ id: 'm1', seq: 1 }) }))

  expect(chats.get('chat-1')!.lastMessage!.text).toBe('newest')
})
