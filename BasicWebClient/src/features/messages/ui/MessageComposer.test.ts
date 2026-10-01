import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useMessagesStore } from '../model/messages.store'
import MessageComposer from './MessageComposer.vue'

vi.mock('../api/messages.api', () => ({ sendTyping: vi.fn(async () => {}), sendMessage: vi.fn() }))

function setup() {
  const pinia = createPinia()
  setActivePinia(pinia)
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
    expect(send).toHaveBeenCalledWith('hello world', [{ type: 'bold', offset: 6, length: 5 }])
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

    expect(send).toHaveBeenCalledWith('hello world', [{ type: 'italic', offset: 6, length: 5 }])
  })

  it('Shift+Enter is a new line, not sending', async () => {
    const { wrapper, send } = setup()
    const area = wrapper.get('textarea')
    await area.setValue('line')
    await area.trigger('keydown', { key: 'Enter', shiftKey: true })
    expect(send).not.toHaveBeenCalled()
  })
})
