import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { useChatsStore } from '@/entities/chat/model/chats.store'
import { chat } from '@/testing/fixtures'
import * as messagesApi from '../api/messages.api'
import { useMessagesStore } from '../model/messages.store'
import MessageComposer from './MessageComposer.vue'

vi.mock('../api/messages.api', () => ({
  sendTyping: vi.fn(async () => {}),
  sendMessage: vi.fn(),
  saveDraft: vi.fn(async () => null),
  removeDraft: vi.fn(async () => {}),
}))

function setup(draftText: string | null = null) {
  const pinia = createPinia()
  setActivePinia(pinia)
  const draft = draftText === null ? null : { text: draftText, entities: [], replyToMessageId: null, updatedAt: '2026-10-01T12:00:00Z' }
  useChatsStore().replaceAll([chat({ draft })], [])
  const store = useMessagesStore()
  store.chatId = 'chat-1'
  const send = vi.fn(() => true)
  store.send = send
  const wrapper = mount(MessageComposer, { global: { plugins: [pinia] }, attachTo: document.body })
  return { wrapper, send }
}

beforeEach(() => {
  document.body.innerHTML = ''
})

describe('MessageComposer', () => {
  it('Ctrl+B over a selection makes it bold; Enter sends text and entities', async () => {
    const { wrapper, send } = setup()
    const area = wrapper.get('textarea')
    await area.setValue('hello world')

    const el = area.element as HTMLTextAreaElement
    el.setSelectionRange(6, 11)
    await area.trigger('select')
    await area.trigger('keydown', { key: 'b', ctrlKey: true })
    expect(wrapper.find('.preview strong').text()).toBe('world')

    await area.trigger('keydown', { key: 'Enter' })
    expect(send).toHaveBeenCalledWith('hello world', [{ type: 'bold', offset: 6, length: 5 }], [])
    expect(el.value).toBe('')
  })

  it('formatting follows the text as it is edited', async () => {
    const { wrapper, send } = setup()
    const area = wrapper.get('textarea')
    await area.setValue('world')
    const el = area.element as HTMLTextAreaElement
    el.setSelectionRange(0, 5)
    await area.trigger('keydown', { key: 'i', ctrlKey: true })

    await area.setValue('hello world')
    await area.trigger('keydown', { key: 'Enter' })

    expect(send).toHaveBeenCalledWith('hello world', [{ type: 'italic', offset: 6, length: 5 }], [])
  })

  it('Shift+Enter is a new line, not sending', async () => {
    const { wrapper, send } = setup()
    const area = wrapper.get('textarea')
    await area.setValue('line')
    await area.trigger('keydown', { key: 'Enter', shiftKey: true })
    expect(send).not.toHaveBeenCalled()
  })
})

describe('drafts', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('the saved draft fills the field when the chat opens', () => {
    const { wrapper } = setup('не дописал')
    expect((wrapper.get('textarea').element as HTMLTextAreaElement).value).toBe('не дописал')
  })

  it('what is typed is saved as the draft after a pause', async () => {
    const { wrapper } = setup()
    await wrapper.get('textarea').setValue('черно')
    await wrapper.get('textarea').setValue('черновик')
    expect(messagesApi.saveDraft).not.toHaveBeenCalled()

    await vi.advanceTimersByTimeAsync(1_500)

    expect(messagesApi.saveDraft).toHaveBeenCalledTimes(1)
    expect(messagesApi.saveDraft).toHaveBeenCalledWith('chat-1', { text: 'черновик', entities: [], replyToMessageId: null })
  })
})
