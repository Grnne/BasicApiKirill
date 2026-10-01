import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'

import { message } from '@/testing/fixtures'
import * as messagesApi from '../api/messages.api'
import { useMessagesStore } from '../model/messages.store'
import ChatSearch from './ChatSearch.vue'

vi.mock('../api/messages.api', async (original) => ({
  ...(await original<typeof import('../api/messages.api')>()),
  searchInChat: vi.fn(),
}))

beforeEach(() => vi.useFakeTimers())
afterEach(() => vi.useRealTimers())

it('searches after a pause in typing and jumps to the chosen message', async () => {
  const pinia = createPinia()
  setActivePinia(pinia)
  const store = useMessagesStore()
  store.chatId = 'chat-1'
  const jumpTo = vi.fn(async () => {})
  store.jumpTo = jumpTo
  const hit = message({ seq: 7, text: 'запускаем завтра' })
  vi.mocked(messagesApi.searchInChat).mockResolvedValue({
    items: [hit], nextCursor: null, hasMore: false, query: 'запу', totalCount: 1,
  })
  const wrapper = mount(ChatSearch, { global: { plugins: [pinia] } })

  await wrapper.get('input[type=search]').setValue('з')
  await wrapper.get('input[type=search]').setValue('запу')
  await vi.advanceTimersByTimeAsync(300)
  await flushPromises()

  expect(messagesApi.searchInChat).toHaveBeenCalledTimes(1)
  expect(messagesApi.searchInChat).toHaveBeenCalledWith('chat-1', 'запу', null, expect.any(AbortSignal))
  expect(wrapper.text()).toContain('Найдено: 1')

  await wrapper.get('.hit').trigger('click')
  expect(jumpTo).toHaveBeenCalledWith(hit.id)
})
