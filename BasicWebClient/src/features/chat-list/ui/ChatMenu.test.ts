import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { expect, it } from 'vitest'

import { chat } from '@/testing/fixtures'
import ChatMenu from './ChatMenu.vue'

it('opened on another chat, the menu starts from the top, not in the mute choice', async () => {
  // The bug: a right click on another row only changed the props; the mute choice stayed open.
  const pinia = createPinia()
  setActivePinia(pinia)
  const wrapper = mount(ChatMenu, { props: { chat: chat({ chatId: 'a' }), x: 0, y: 0 }, global: { plugins: [pinia] } })
  await wrapper.findAll('button').find((b) => b.text() === 'Без звука…')!.trigger('click')
  expect(wrapper.text()).toContain('На 1 час')

  await wrapper.setProps({ chat: chat({ chatId: 'b' }) })

  expect(wrapper.text()).toContain('Без звука…')
  expect(wrapper.text()).not.toContain('На 1 час')
})
