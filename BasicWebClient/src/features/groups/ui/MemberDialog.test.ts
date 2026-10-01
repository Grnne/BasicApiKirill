import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, expect, it, vi } from 'vitest'

import * as chatApi from '@/entities/chat/api'
import type { ChatDetail } from '@/entities/chat/types'
import type { GroupMemberDto, GroupPermissionsDto } from '@/shared/api/schema'
import MemberDialog from './MemberDialog.vue'

vi.mock('@/entities/chat/api', () => ({
  getChatDetail: vi.fn(),
  getMembers: vi.fn(),
  setRole: vi.fn(),
  setMemberPermissions: vi.fn(),
}))
vi.mock('@/entities/media/api', () => ({ getLinks: vi.fn(async () => ({ items: [] })) }))

const perms = (p: Partial<GroupPermissionsDto> = {}): GroupPermissionsDto => ({
  addAdmins: true, addMembers: true, changeInfo: true, deleteMessages: true, removeMembers: true,
  sendMedia: true, sendMessages: true, ...p,
})
const bob: GroupMemberDto = {
  userId: 'bob', username: 'bob', displayName: 'Bob', avatarId: null, role: 'member',
  permissions: perms({ addAdmins: false, deleteMessages: false, removeMembers: false, changeInfo: false }),
}
const detail: ChatDetail = {
  chatId: 'g1', type: 'group', title: 'Team', avatarId: null, createdBy: 'me', myRole: 'owner',
  myPermissions: perms(), memberPermissions: perms(),
  participants: [
    { userId: 'me', username: 'me', displayName: 'Me', avatarId: null, role: 'owner' },
    { userId: 'bob', username: 'bob', displayName: 'Bob', avatarId: null, role: 'member' },
  ],
}

beforeEach(() => {
  setActivePinia(createPinia())
  vi.mocked(chatApi.getChatDetail).mockResolvedValue(structuredClone(detail))
  vi.mocked(chatApi.getMembers).mockResolvedValue([bob])
  vi.mocked(chatApi.setMemberPermissions).mockImplementation(async (_c, _u, patch) => ({
    ...bob, permissions: { ...bob.permissions, ...patch } as GroupPermissionsDto,
  }))
})

it('saved permissions replace the overrides as a whole: every one shown is sent', async () => {
  const wrapper = mount(MemberDialog, { props: { chatId: 'g1', meId: 'me', userId: 'bob' }, attachTo: document.body })
  await flushPromises()

  const boxes = wrapper.findAll('input[type=checkbox]')
  expect(boxes).toHaveLength(4)
  await boxes[1]!.setValue(false) // sendMedia
  await wrapper.findAll('button').find((b) => b.text() === 'Сохранить права')!.trigger('click')
  await flushPromises()

  expect(chatApi.setMemberPermissions).toHaveBeenCalledWith('g1', 'bob', {
    sendMessages: true, sendMedia: false, addMembers: true, changeInfo: false,
  })
  wrapper.unmount()
})

it('"as in the group" removes the overrides', async () => {
  const wrapper = mount(MemberDialog, { props: { chatId: 'g1', meId: 'me', userId: 'bob' }, attachTo: document.body })
  await flushPromises()

  await wrapper.findAll('button').find((b) => b.text() === 'Как в группе')!.trigger('click')
  await flushPromises()

  expect(chatApi.setMemberPermissions).toHaveBeenCalledWith('g1', 'bob', {})
  wrapper.unmount()
})
