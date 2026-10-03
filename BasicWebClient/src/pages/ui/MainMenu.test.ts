import { mount } from '@vue/test-utils'
import { afterEach, expect, it } from 'vitest'

import MainMenu from './MainMenu.vue'

afterEach(() => {
  document.body.innerHTML = ''
})

it('the menu names what it does, and closes once an item is picked', async () => {
  const wrapper = mount(MainMenu, { attachTo: document.body })
  expect(wrapper.find('[role=menu]').exists()).toBe(false)

  await wrapper.get('[aria-label="Меню"]').trigger('click')
  expect(wrapper.findAll('[role=menuitem]').map((b) => b.text())).toEqual([
    '👥 Новая группа', '★ Избранное', '⚙ Настройки', 'Выйти',
  ])

  await wrapper.findAll('[role=menuitem]')[1]!.trigger('click')
  expect(wrapper.emitted('saved')).toHaveLength(1)
  expect(wrapper.find('[role=menu]').exists()).toBe(false)
})

it('Escape closes it', async () => {
  const wrapper = mount(MainMenu, { attachTo: document.body })
  await wrapper.get('[aria-label="Меню"]').trigger('click')

  await wrapper.get('[role=menu]').trigger('keydown', { key: 'Escape' })

  expect(wrapper.find('[role=menu]').exists()).toBe(false)
})
