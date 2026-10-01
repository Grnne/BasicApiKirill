import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import * as chatApi from '@/entities/chat/api'
import { useChatsStore } from '@/entities/chat/model/chats.store'
import type { ChatDetail } from '@/entities/chat/types'
import type { GroupPermissionsDto } from '@/shared/api/schema'
import { BOB, ME, chat, message } from '@/testing/fixtures'
import MessageItem from './MessageItem.vue'

vi.mock('@/entities/chat/api', async (original) => ({
  ...(await original<typeof import('@/entities/chat/api')>()),
  getChatDetail: vi.fn(),
}))

const perms = (p: Partial<GroupPermissionsDto> = {}): GroupPermissionsDto => ({
  addAdmins: false, addMembers: false, changeInfo: false, deleteMessages: false, removeMembers: false,
  sendMedia: true, sendMessages: true, ...p,
})

function detail(my: Partial<GroupPermissionsDto>): ChatDetail {
  return {
    chatId: 'chat-1', type: 'group', title: 'G', avatarId: null, createdBy: ME, myRole: 'admin',
    myPermissions: perms(my), memberPermissions: perms(), participants: [],
  }
}

async function deleteDialog(my: Partial<GroupPermissionsDto>) {
  const pinia = createPinia()
  setActivePinia(pinia)
  useChatsStore().replaceAll([chat({ type: 'group', title: 'G' })], [])
  vi.mocked(chatApi.getChatDetail).mockResolvedValue(detail(my))
  const wrapper = mount(MessageItem, {
    props: { message: message({ senderId: BOB, text: 'чужое' }), meId: ME },
    global: { plugins: [pinia] },
    attachTo: document.body,
  })
  await flushPromises()
  await wrapper.get('button.more').trigger('click')
  await wrapper.get('.menu .danger').trigger('click')
  return wrapper
}

beforeEach(() => {
  document.body.innerHTML = ''
})

describe('deleting another member\'s message in a group', () => {
  it('an admin with the right deletes it for everyone', async () => {
    const wrapper = await deleteDialog({ deleteMessages: true })
    expect(wrapper.text()).toContain('Удалить у всех')
  })

  it('without the right — only for oneself', async () => {
    const wrapper = await deleteDialog({ deleteMessages: false })
    expect(wrapper.text()).not.toContain('Удалить у всех')
    expect(wrapper.text()).toContain('только у вас')
  })
})

describe('the edit window', () => {
  it('is counted when the menu opens, not when the message was first drawn', async () => {
    // The bug: the time was taken once, and "Изменить" stayed after the 48 hours had passed.
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-10-01T12:00:00Z'))
    const pinia = createPinia()
    setActivePinia(pinia)
    useChatsStore().replaceAll([chat()], [])
    const wrapper = mount(MessageItem, {
      props: { message: message({ senderId: ME, text: 'моё', createdAt: '2026-09-29T12:01:00Z' }), meId: ME },
      global: { plugins: [pinia] },
      attachTo: document.body,
    })
    await wrapper.get('button.more').trigger('click')
    expect(wrapper.text()).toContain('Изменить')
    await wrapper.get('button.more').trigger('click')

    vi.setSystemTime(new Date('2026-10-01T12:02:00Z'))
    await wrapper.get('button.more').trigger('click')

    expect(wrapper.text()).not.toContain('Изменить')
    vi.useRealTimers()
  })
})
