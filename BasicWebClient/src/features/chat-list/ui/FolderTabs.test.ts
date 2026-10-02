import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { expect, it } from 'vitest'

import { useChatsStore } from '@/entities/chat/model/chats.store'
import FolderTabs from './FolderTabs.vue'

it('the page actions sit in the folder row, outside the tabs that scroll', () => {
  const pinia = createPinia()
  setActivePinia(pinia)
  const folders = Array.from({ length: 12 }, (_, i) => ({
    id: `f${i}`, title: `Папка ${i}`, includePrivate: false, includeGroups: false, onlyUnread: false,
    chatIds: [], pinnedChatIds: [],
  }))
  useChatsStore().replaceAll([], folders)

  const wrapper = mount(FolderTabs, {
    global: { plugins: [pinia] },
    slots: { actions: '<button title="Новая группа">👥</button>' },
  })

  expect(wrapper.find('.actions [title="Новая группа"]').exists()).toBe(true)
  expect(wrapper.find('.tabs [title="Новая группа"]').exists()).toBe(false)
})
