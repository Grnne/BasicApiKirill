import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'

import * as chatApi from '@/entities/chat/api'
import { useChatsStore } from '@/entities/chat/model/chats.store'
import * as usersApi from '@/entities/user/api'
import { chat } from '@/testing/fixtures'
import CreateGroupDialog from './CreateGroupDialog.vue'

vi.mock('@/entities/chat/api', () => ({ createGroup: vi.fn(), setChatAvatar: vi.fn() }))
vi.mock('@/entities/user/api', () => ({ searchUsers: vi.fn() }))

beforeEach(() => {
  setActivePinia(createPinia())
  vi.useFakeTimers()
  vi.mocked(usersApi.searchUsers).mockResolvedValue({
    items: [{ userId: 'bob', username: 'bob', displayName: 'Bob', avatarId: null, avatarUrl: null }],
  } as never)
  vi.mocked(chatApi.createGroup).mockResolvedValue(chat({ chatId: 'g1', type: 'group', title: 'Team' }))
})

afterEach(() => vi.useRealTimers())

it('a title and picked members make the group; it lands in the list', async () => {
  const wrapper = mount(CreateGroupDialog, { attachTo: document.body })
  const create = () => wrapper.get('button[type=submit]')
  expect(create().attributes('disabled')).toBeDefined()

  await wrapper.get('input[type=text]').setValue('  Team ')
  await wrapper.get('input[type=search]').setValue('bo')
  await vi.advanceTimersByTimeAsync(300)
  await flushPromises()
  await wrapper.get('.user').trigger('click')
  expect(wrapper.get('.chips').text()).toContain('Bob')

  await wrapper.get('form').trigger('submit')
  await flushPromises()

  expect(chatApi.createGroup).toHaveBeenCalledWith('Team', ['bob'])
  expect(useChatsStore().get('g1')).not.toBeNull()
  expect(wrapper.emitted('created')).toEqual([['g1']])
  wrapper.unmount()
})

it('a failed create keeps the dialog open with the reason', async () => {
  vi.mocked(chatApi.createGroup).mockRejectedValue(new Error('offline'))
  const wrapper = mount(CreateGroupDialog, { attachTo: document.body })

  await wrapper.get('input[type=text]').setValue('Team')
  await wrapper.get('form').trigger('submit')
  await flushPromises()

  expect(wrapper.emitted('created')).toBeUndefined()
  expect(wrapper.find('.error').exists()).toBe(true)
  wrapper.unmount()
})
